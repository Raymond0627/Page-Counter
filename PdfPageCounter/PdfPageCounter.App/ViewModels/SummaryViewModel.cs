using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PdfPageCounter_App.ViewModels;

/// <summary>
/// Bindable summary metrics, updated live as the scan progresses.
/// </summary>
public sealed class SummaryViewModel : INotifyPropertyChanged
{
    private long _totalPages;
    private long _totalFiles;
    private long _duplicateFileCount;
    private long _uniquePageTotal;
    private long _duplicatePages;
    private long _errorCount;
    private long _skippedFolderCount;
    private bool _isScanning;
    private bool _isCancelled;
    private bool _hasResults;
    private string _progressText = string.Empty;
    private string _scanningLabel = string.Empty;
    private string _scopeText = string.Empty;

    /// <summary>
    /// Describes which folder the Total Pages / Total Files cards currently
    /// describe, so the numbers are never ambiguous after a folder is selected.
    /// </summary>
    public string ScopeText
    {
        get => _scopeText;
        set => SetField(ref _scopeText, value);
    }

    public long TotalPages
    {
        get => _totalPages;
        set => SetField(ref _totalPages, value);
    }

    public long TotalFiles
    {
        get => _totalFiles;
        set => SetField(ref _totalFiles, value);
    }

    public long DuplicateFileCount
    {
        get => _duplicateFileCount;
        set => SetField(ref _duplicateFileCount, value);
    }

    public long UniquePageTotal
    {
        get => _uniquePageTotal;
        set => SetField(ref _uniquePageTotal, value);
    }

    /// <summary>Pages belonging to duplicate copies only (originals excluded).</summary>
    public long DuplicatePages
    {
        get => _duplicatePages;
        set => SetField(ref _duplicatePages, value);
    }

    public long ErrorCount
    {
        get => _errorCount;
        set => SetField(ref _errorCount, value);
    }

    public long SkippedFolderCount
    {
        get => _skippedFolderCount;
        set => SetField(ref _skippedFolderCount, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set => SetField(ref _isScanning, value);
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        set => SetField(ref _isCancelled, value);
    }

    public bool HasResults
    {
        get => _hasResults;
        set => SetField(ref _hasResults, value);
    }

    public bool IsNotScanning => !IsScanning;

    /// <summary>True when a scope has been established, so the label can be shown.</summary>
    public bool HasScope => !string.IsNullOrEmpty(ScopeText);

    public bool ShowCancelledBanner => IsCancelled && HasResults;

    public string ProgressText
    {
        get => _progressText;
        set => SetField(ref _progressText, value);
    }

    /// <summary>
    /// Animated "Scanning…" label. Its trailing dots cycle so the in-progress
    /// state is visible even when the file counter is not changing quickly.
    /// </summary>
    public string ScanningLabel
    {
        get => _scanningLabel;
        set => SetField(ref _scanningLabel, value);
    }

    public void Reset()
    {
        TotalPages = 0;
        TotalFiles = 0;
        DuplicateFileCount = 0;
        UniquePageTotal = 0;
        DuplicatePages = 0;
        ErrorCount = 0;
        SkippedFolderCount = 0;
        IsCancelled = false;
        HasResults = false;
        IsScanning = false;
        ProgressText = string.Empty;
        ScanningLabel = string.Empty;
        ScopeText = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            if (propertyName == nameof(TotalPages))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPagesText)));
            }
            else if (propertyName == nameof(TotalFiles))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalFilesText)));
            }
            else if (propertyName == nameof(DuplicateFileCount))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DuplicateFileCountText)));
            }
            else if (propertyName == nameof(UniquePageTotal))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UniquePageTotalText)));
            }
            else if (propertyName == nameof(DuplicatePages))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DuplicatePagesText)));
            }
            else if (propertyName == nameof(ScopeText))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasScope)));
            }
            else if (propertyName == nameof(ErrorCount))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorCountText)));
            }

            // Derived properties that depend on these.
            if (propertyName == nameof(IsScanning))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNotScanning)));
            }
            if (propertyName == nameof(IsCancelled) || propertyName == nameof(HasResults))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCancelledBanner)));
            }
        }
    }

    public string TotalPagesText => TotalPages.ToString("N0");
    public string TotalFilesText => TotalFiles.ToString("N0");
    public string DuplicateFileCountText => DuplicateFileCount.ToString("N0");
    public string UniquePageTotalText => UniquePageTotal.ToString("N0");
    public string DuplicatePagesText => DuplicatePages.ToString("N0");
    public string ErrorCountText => ErrorCount.ToString("N0");
}
