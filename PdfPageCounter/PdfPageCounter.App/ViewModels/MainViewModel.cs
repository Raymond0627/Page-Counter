using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;
using PdfPageCounter_App.Services;
using Windows.ApplicationModel.DataTransfer;

namespace PdfPageCounter_App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;
    private readonly PdfPageCounterService _pageCounter;
    private readonly FolderScanner _scanner;
    private readonly DuplicateDetector _duplicateDetector;
    private readonly DuplicateIsolator _isolator = new();

    private CancellationTokenSource? _cts;
    private DispatcherQueueTimer? _refreshTimer;

    // True once the user asked to stop, until the current scan finishes.
    private bool _cancelRequested;

    // UI-thread snapshot counters produced by the periodic timer.
    private long _displayTotalFiles;
    private long _displayTotalPages;
    private long _displayErrorCount;
    private long _displaySkippedFolders;

    // Drives the pulsing ellipsis on the "Scanning..." label.
    private int _dotFrame;
    private int _dotTick;

    private FolderNode? _rootModel;

    public SummaryViewModel Summary { get; } = new();

    public SettingsViewModel Settings { get; } = new(new SettingsStore());

    public ObservableCollection<FolderNodeViewModel> TreeRoots { get; } = new();

    public ObservableCollection<PdfFileRecord> SelectedFolderFiles { get; } = new();

    [ObservableProperty]
    private string _networkPath = string.Empty;

    [ObservableProperty]
    private string _currentRoot = string.Empty;

    [ObservableProperty]
    private FolderNodeViewModel? _selectedFolder;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _canIsolate;

    [ObservableProperty]
    private bool _canRevert;

    [ObservableProperty]
    private string? _currentIsolationRoot;

    [ObservableProperty]
    private int _duplicateGroupCount;

    [ObservableProperty]
    private int _isolatableFileCount;

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _pageCounter = new PdfPageCounterService();
        _duplicateDetector = new DuplicateDetector();
        _scanner = new FolderScanner(_pageCounter, _duplicateDetector);

        _scanner.Progress += OnScanProgress;
        _scanner.FileProcessed += OnFileProcessed;
        _scanner.FolderSkipped += OnFolderSkipped;
    }

    partial void OnSelectedFolderChanged(FolderNodeViewModel? value)
    {
        SelectedFolderFiles.Clear();
        if (value?.Node != null)
        {
            var files = value.Node.Files
                .OrderBy(f => f.IsError)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                SelectedFolderFiles.Add(file);
            }
        }

        ApplyScope(value?.Node);
    }

    /// <summary>
    /// Points the Total Pages / Total Files cards at the given folder. Both
    /// figures are read from the folder tree rather than the live scan counters,
    /// so they are exact and cover the folder plus all of its subfolders.
    /// Selecting the root folder shows the whole scan.
    /// </summary>
    private void ApplyScope(FolderNode? node)
    {
        var target = node ?? _rootModel;
        if (target is null)
        {
            Summary.TotalFiles = 0;
            Summary.TotalPages = 0;
            Summary.ScopeText = string.Empty;
            return;
        }

        Summary.TotalFiles = target.TotalFileCount;
        Summary.TotalPages = target.TotalPageCount;
        Summary.ScopeText = ReferenceEquals(target, _rootModel)
            ? $"Whole scan — {target.FullPath}"
            : target.FullPath;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (Summary.IsScanning)
        {
            return;
        }

        var path = NetworkPathValidator.Normalize(NetworkPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            SetError("Please browse to a folder or enter a network path.");
            return;
        }

        var validationError = NetworkPathValidator.Validate(path);
        if (validationError != null)
        {
            SetError(validationError);
            return;
        }

        var reachabilityError = NetworkPathValidator.CheckReachability(path);
        if (reachabilityError != null)
        {
            SetError(reachabilityError);
            return;
        }

        await RunScanAsync(path);
    }

    private async Task RunScanAsync(string path)
    {
        BeginNewScan(path);
        var cts = _cts!;

        try
        {
            var root = await _scanner.ScanAsync(path, cts.Token);
            _rootModel = root;
            var cancelled = cts.IsCancellationRequested;

            await _dispatcher.EnqueueAsync(() =>
            {
                // Clear the scanning flag first so the live-refresh timer can no
                // longer overwrite the scoped values we are about to set.
                Summary.IsScanning = false;

                if (cancelled)
                {
                    Summary.IsCancelled = true;
                    BuildTreeFromModel(root);
                    RefreshSummaryFromModel(root);
                    Summary.ProgressText = "Scan cancelled — results are partial.";
                }
                else
                {
                    BuildTreeFromModel(root);
                    RefreshSummaryFromModel(root);
                    Summary.ProgressText = $"Done — {Summary.TotalFiles:N0} files, {Summary.TotalPages:N0} pages.";
                }

                UpdateIsolateAvailability(root);
            });
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.EnqueueAsync(() =>
            {
                Summary.IsScanning = false;
                if (_rootModel != null)
                {
                    Summary.IsCancelled = true;
                    BuildTreeFromModel(_rootModel);
                    RefreshSummaryFromModel(_rootModel);
                    UpdateIsolateAvailability(_rootModel);
                }
                Summary.ProgressText = "Scan cancelled — results are partial.";
            });
        }
        finally
        {
            StopTimers();
            await _dispatcher.EnqueueAsync(() =>
            {
                Summary.IsScanning = false;
                Summary.HasResults = true;
            });
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Summary.IsScanning && _cts != null)
        {
            _cancelRequested = true;
            _cts.Cancel();
            Summary.ProgressText = "Cancelling…";
        }
    }

    // ---------------- Duplicate isolation ----------------

    [RelayCommand]
    private async Task IsolateDuplicatesAsync()
    {
        if (_rootModel is null || Summary.IsScanning)
        {
            return;
        }

        try
        {
            var path = _rootModel.FullPath;
            var groups = _duplicateDetector.GetDuplicateGroups();
            var result = await Task.Run(() => _isolator.Isolate(path, groups));

            if (Settings.AutoRescanAfterIsolate)
            {
                await RunScanAsync(path);
                Summary.ProgressText = $"Isolated {result.MovedCount:N0} duplicate(s) into {result.DestinationRoot} — re-scanned.";
            }
            else
            {
                CanRevert = _isolator.CanRevert(path);
                CanIsolate = _duplicateDetector.GetDuplicateGroups().Count > 0;
                Summary.ProgressText = $"Isolated {result.MovedCount:N0} duplicate(s) into {result.DestinationRoot}.";
            }

            if (result.FailedCount > 0)
            {
                SetError($"{result.FailedCount} file(s) could not be isolated.");
            }
        }
        catch (Exception ex)
        {
            SetError($"Isolation failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RevertIsolationAsync()
    {
        if (_rootModel is null || Summary.IsScanning)
        {
            return;
        }

        try
        {
            var path = _rootModel.FullPath;
            var result = await Task.Run(() => _isolator.Revert(path));

            if (Settings.AutoRescanAfterIsolate)
            {
                await RunScanAsync(path);
                Summary.ProgressText = $"Reverted {result.MovedCount:N0} file(s) to their original folders — re-scanned.";
            }
            else
            {
                CanRevert = _isolator.CanRevert(path);
                CanIsolate = _duplicateDetector.GetDuplicateGroups().Count > 0;
                Summary.ProgressText = $"Reverted {result.MovedCount:N0} file(s).";
            }

            if (result.FailedCount > 0)
            {
                SetError($"{result.FailedCount} file(s) could not be reverted.");
            }
        }
        catch (Exception ex)
        {
            SetError($"Revert failed: {ex.Message}");
        }
    }

    private void UpdateIsolateAvailability(FolderNode root)
    {
        var groups = _duplicateDetector.GetDuplicateGroups();
        DuplicateGroupCount = groups.Count;

        var destRoot = Path.Combine(root.FullPath, "duplicates");
        IsolatableFileCount = groups.Sum(g =>
        {
            var outside = g.Count(f => !f.FullPath.StartsWith(destRoot + "\\", StringComparison.OrdinalIgnoreCase));
            return Math.Max(0, outside - 1);
        });

        CanIsolate = IsolatableFileCount > 0;
        CanRevert = _isolator.CanRevert(root.FullPath);
        CurrentIsolationRoot = root.FullPath;
    }

    /// <summary>
    /// Copies the kept original's path of the duplicate group containing
    /// <paramref name="record"/> to the clipboard.
    /// </summary>
    public void CopyOriginalPath(PdfFileRecord record)
    {
        if (!TryFindGroup(record, out var group))
        {
            Summary.ProgressText = "This file is not part of a duplicate group.";
            return;
        }

        var originals = GatherPaths(group, m => m.IsOriginal);
        if (originals.Count == 0)
        {
            Summary.ProgressText = "No original found to copy.";
            return;
        }

        CopyToClipboard(originals);
        Summary.ProgressText = $"Copied {originals.Count:N0} original path(s) to clipboard.";
    }

    /// <summary>
    /// Copies the path(s) of every duplicate copy of the group containing
    /// <paramref name="record"/> to the clipboard (newline-separated).
    /// </summary>
    public void CopyDuplicatePaths(PdfFileRecord record)
    {
        if (!TryFindGroup(record, out var group))
        {
            Summary.ProgressText = "This file is not part of a duplicate group.";
            return;
        }

        var duplicates = GatherPaths(group, m => m.IsDuplicate);
        if (duplicates.Count == 0)
        {
            Summary.ProgressText = "No duplicate copies found to copy.";
            return;
        }

        CopyToClipboard(duplicates);
        Summary.ProgressText = $"Copied {duplicates.Count:N0} duplicate path(s) to clipboard.";
    }

    private bool TryFindGroup(PdfFileRecord record, out IReadOnlyList<PdfFileRecord> group)
    {
        var groups = _duplicateDetector.GetDuplicateGroups();
        foreach (var candidate in groups)
        {
            var isMember = candidate.Any(m =>
                string.Equals(m.FullPath, record.FullPath, StringComparison.OrdinalIgnoreCase));
            if (isMember)
            {
                group = candidate;
                return true;
            }
        }
        group = Array.Empty<PdfFileRecord>();
        return false;
    }

    private static List<string> GatherPaths(IReadOnlyList<PdfFileRecord> group, Func<PdfFileRecord, bool> predicate) =>
        group
            .Where(predicate)
            .Select(m => m.FullPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void CopyToClipboard(List<string> paths)
    {
        var data = new DataPackage();
        data.SetText(string.Join(Environment.NewLine, paths));
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    public void BrowseSelected(string? folderPath)
    {
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            NetworkPath = folderPath;
            CurrentRoot = folderPath;
        }
    }

    // ---------------- Scan lifecycle ----------------

    private void BeginNewScan(string path)
    {
        ClearError();
        Summary.Reset();
        _duplicateDetector.Reset();
        SelectedFolderFiles.Clear();
        TreeRoots.Clear();
        _rootModel = null;

        _displayTotalFiles = 0;
        _displayTotalPages = 0;
        _displayErrorCount = 0;
        _displaySkippedFolders = 0;

        CurrentRoot = path;
        _cts = new CancellationTokenSource();
        _cancelRequested = false;
        Summary.IsScanning = true;
        Summary.ProgressText = "Preparing…";
        _dotFrame = 0;
        _dotTick = 0;
        Summary.ScanningLabel = "Scanning.";

        StartTimers();
    }

    // ---------------- Scanner events (background threads) ----------------

    private void OnScanProgress(long processed, long discovered)
    {
        // Update display counters on background threads; the UI reads them on the
        // timer to avoid flooding the dispatcher with per-file callbacks.
        Interlocked.Exchange(ref _displayTotalFiles, processed);
        _ = discovered;
    }

    private void OnFileProcessed(PdfFileRecord record)
    {
        if (record.IsError)
        {
            Interlocked.Increment(ref _displayErrorCount);
        }
        else
        {
            Interlocked.Add(ref _displayTotalPages, record.PageCount);
        }
    }

    private void OnFolderSkipped(string folder, string reason)
    {
        Interlocked.Increment(ref _displaySkippedFolders);
        _ = folder;
        _ = reason;
    }

    // ---------------- Timers (UI thread) ----------------

    private void StartTimers()
    {
        _refreshTimer = _dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick += (_, _) => RefreshLiveCounters();
        _refreshTimer.Start();
    }

    private void StopTimers()
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer = null;
        }
    }

    private void RefreshLiveCounters()
    {
        // While a scan is running the cards show live progress. Once it finishes
        // the cards are driven by the selected folder instead (see ApplyScope),
        // so stop writing to them here.
        if (Summary.IsScanning)
        {
            Summary.TotalFiles = _displayTotalFiles;
            Summary.TotalPages = _displayTotalPages;
            Summary.ProgressText = $"{_displayTotalFiles:N0} files processed";

            // Advance the pulsing ellipsis roughly every half second (5 x 100 ms).
            if (++_dotTick % 5 == 0)
            {
                _dotFrame = (_dotFrame + 1) % 3;
            }
            Summary.ScanningLabel = (_cancelRequested ? "Stopping" : "Scanning") + new string('.', _dotFrame + 1);
        }

        Summary.ErrorCount = _displayErrorCount;
        Summary.SkippedFolderCount = _displaySkippedFolders;
    }

    private void RefreshSummaryFromModel(FolderNode root)
    {
        // Re-apply the current scope (whole scan if nothing is selected) so the
        // cards agree with the tree that was just built.
        ApplyScope(SelectedFolder?.Node);

        Summary.ErrorCount = _displayErrorCount;
        Summary.SkippedFolderCount = _displaySkippedFolders;

        // Duplicate figures after Resolve().
        Summary.DuplicateFileCount = _duplicateDetector.DuplicateFileCount();
        Summary.UniquePageTotal = _duplicateDetector.UniquePageTotal();
        Summary.DuplicatePages = _duplicateDetector.DuplicatePageCount();
    }

    // ---------------- Tree building ----------------

    private void BuildTreeFromModel(FolderNode root)
    {
        // Remember the selected folder by path so it can be re-resolved against
        // the tree that is about to replace the current one. Without this the
        // selection would keep pointing at a discarded FolderNode and the cards
        // would report stale numbers after a rescan.
        var selectedPath = SelectedFolder?.FullPath;

        var vm = BuildBranch(root);
        TreeRoots.Clear();
        TreeRoots.Add(vm);

        SelectedFolder = (selectedPath is null ? null : FindByPath(vm, selectedPath)) ?? vm;
    }

    private static FolderNodeViewModel? FindByPath(FolderNodeViewModel node, string path)
    {
        if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var match = FindByPath(child, path);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private FolderNodeViewModel BuildBranch(FolderNode node)
    {
        var vm = new FolderNodeViewModel(node);
        foreach (var child in node.Children)
        {
            vm.Children.Add(BuildBranch(child));
        }
        vm.RefreshLabel();
        return vm;
    }

    // ---------------- Helpers ----------------

    private void SetError(string message)
    {
        HasError = true;
        ErrorMessage = message;
    }

    private void ClearError()
    {
        HasError = false;
        ErrorMessage = string.Empty;
    }
}

public static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue dispatcher, Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (dispatcher.HasThreadAccess)
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            return tcs.Task;
        }

        if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Unable to enqueue on dispatcher."));
        }
        return tcs.Task;
    }
}
