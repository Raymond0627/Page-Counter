using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;

namespace PdfPageCounter_App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;
    private readonly PdfPageCounterService _pageCounter;
    private readonly FolderScanner _scanner;
    private readonly DuplicateDetector _duplicateDetector;

    private CancellationTokenSource? _cts;
    private DispatcherQueueTimer? _refreshTimer;

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

        BeginNewScan(path);
        var cts = _cts!;

        try
        {
            var root = await _scanner.ScanAsync(path, cts.Token);
            _rootModel = root;

            await _dispatcher.EnqueueAsync(() =>
            {
                // Clear the scanning flag first so the live-refresh timer can no
                // longer overwrite the scoped values we are about to set.
                Summary.IsScanning = false;
                BuildTreeFromModel(root);
                RefreshSummaryFromModel(root);
                Summary.ProgressText = $"Done — {Summary.TotalFiles:N0} files, {Summary.TotalPages:N0} pages.";
            });
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.EnqueueAsync(() =>
            {
                Summary.IsScanning = false;
                if (_rootModel != null)
                {
                    BuildTreeFromModel(_rootModel);
                    RefreshSummaryFromModel(_rootModel);
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
            _cts.Cancel();
        }
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
            Summary.ScanningLabel = "Scanning" + new string('.', _dotFrame + 1);
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
