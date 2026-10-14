using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;

namespace PdfPageCounter_App.ViewModels;

/// <summary>One row in the Box Count results table.</summary>
public sealed class BoxCountRow
{
    public string Label { get; init; } = string.Empty;

    public long Count { get; init; }

    public long PageCount { get; init; }

    public bool IsNoBox { get; init; }

    /// <summary>Folder numbers (box rows) or relative folder paths (No box row).</summary>
    public IReadOnlyList<FolderEntry> Folders { get; init; } = Array.Empty<FolderEntry>();

    public string CountText => Count.ToString("N0");

    public string PageCountText => PageCount.ToString("N0");

    public bool HasFolders => Folders.Count > 0;

    public bool HasNoFolders => Folders.Count == 0;

    /// <summary>Collapsed cell text: the first folder plus a "+N" hint.</summary>
    public string FolderSummaryText
    {
        get
        {
            if (Folders.Count == 0)
            {
                return "—";
            }

            var first = Folders[0].DisplayText;
            return Folders.Count > 1 ? $"{first}  +{Folders.Count - 1}" : first;
        }
    }
}

/// <summary>
/// Drives the Box Count tab: its own scan (separate from the main Scan tab),
/// grouped by box-number folder, with file and page totals, plus Excel export.
/// </summary>
public sealed partial class BoxCountViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;
    private readonly BoxCounter _counter;
    private readonly BoxReportExporter _exporter = new();

    private CancellationTokenSource? _cts;
    private DispatcherQueueTimer? _timer;
    private BoxScanResult? _lastResult;

    private long _filesFound;
    private long _dirs;
    private int _dotFrame;
    private int _dotTick;

    public BoxCountViewModel(DispatcherQueue dispatcher, PdfPageCounterService pageCounter)
    {
        _dispatcher = dispatcher;
        _counter = new BoxCounter(pageCounter);
        _counter.Progress += OnProgress;
    }

    public ObservableCollection<BoxCountRow> Results { get; } = new();

    [ObservableProperty]
    private string _boxPath = string.Empty;

    [ObservableProperty]
    private string _scopeText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _scanningLabel = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _canExport;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public void BrowseSelected(string? folderPath)
    {
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            BoxPath = folderPath;
            ScopeText = folderPath;
        }
    }

    [RelayCommand]
    private async Task ScanBoxesAsync()
    {
        if (IsScanning)
        {
            return;
        }

        var path = NetworkPathValidator.Normalize(BoxPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            SetError("Please browse to a folder or enter a path.");
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

        ClearError();
        Results.Clear();
        _lastResult = null;
        CanExport = false;
        HasResults = false;
        Interlocked.Exchange(ref _filesFound, 0);
        Interlocked.Exchange(ref _dirs, 0);
        _dotFrame = 0;
        _dotTick = 0;

        BoxPath = path;
        ScopeText = path;
        IsScanning = true;
        ScanningLabel = "Scanning.";
        StatusText = "Preparing…";
        StartTimer();

        var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            var result = await _counter.ScanAsync(path, cts.Token);
            _lastResult = result;

            await _dispatcher.EnqueueAsync(() =>
            {
                IsScanning = false;
                BuildRows(result);
                HasResults = true;
                CanExport = true;
                StatusText = $"Done — {result.Boxes.Count:N0} box(es), {result.TotalFiles:N0} PDF file(s), {result.TotalPages:N0} page(s).";
            });
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.EnqueueAsync(() =>
            {
                IsScanning = false;
                StatusText = "Box scan cancelled — results are partial.";
            });
        }
        finally
        {
            StopTimer();
            await _dispatcher.EnqueueAsync(() => IsScanning = false);
        }
    }

    [RelayCommand]
    private void CancelBoxes()
    {
        if (IsScanning && _cts != null)
        {
            ScanningLabel = "Stopping.";
            StatusText = "Cancelling…";
            _cts.Cancel();
        }
    }

    public async Task ExportToAsync(string filePath)
    {
        if (_lastResult is null)
        {
            StatusText = "Run a box scan first.";
            return;
        }

        try
        {
            var result = _lastResult;
            await Task.Run(() => _exporter.Export(result, filePath));
            StatusText = $"Exported to {filePath}";
        }
        catch (Exception ex)
        {
            SetError($"Export failed: {ex.Message}");
        }
    }

    private void BuildRows(BoxScanResult result)
    {
        Results.Clear();
        foreach (var box in result.Boxes)
        {
            Results.Add(new BoxCountRow
            {
                Label = box.Label,
                Count = box.FileCount,
                PageCount = box.PageCount,
                Folders = box.FolderNumbers.Select(n => new FolderEntry { Text = n }).ToList(),
            });
        }
        Results.Add(new BoxCountRow
        {
            Label = "No box",
            Count = result.UnboxedFileCount,
            PageCount = result.UnboxedPageCount,
            IsNoBox = true,
            Folders = result.UnboxedFolders.ToList(),
        });
    }

    private void OnProgress(long files, long dirs)
    {
        Interlocked.Exchange(ref _filesFound, files);
        Interlocked.Exchange(ref _dirs, dirs);
    }

    private void StartTimer()
    {
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => RefreshLive();
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    private void RefreshLive()
    {
        if (!IsScanning)
        {
            return;
        }

        if (++_dotTick % 5 == 0)
        {
            _dotFrame = (_dotFrame + 1) % 3;
        }
        ScanningLabel = "Scanning" + new string('.', _dotFrame + 1);
        StatusText = $"{Interlocked.Read(ref _filesFound):N0} PDF file(s) found";
    }

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