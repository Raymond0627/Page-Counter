using CommunityToolkit.Mvvm.ComponentModel;
using PdfPageCounter_App.Services;

namespace PdfPageCounter_App.ViewModels;

/// <summary>
/// Settings tab state. Every toggle is persisted immediately so changes survive
/// restarts.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;

    public SettingsViewModel(SettingsStore store)
    {
        _store = store;
        var saved = store.Load();
        ShowDuplicateCount = saved.ShowDuplicateCount;
        ShowUniquePageCount = saved.ShowUniquePageCount;
        ShowDuplicatePages = saved.ShowDuplicatePages;
        MatchFileName = saved.MatchFileName;
        MatchPageCount = saved.MatchPageCount;
        MatchFileSize = saved.MatchFileSize;
        AutoRescanAfterIsolate = saved.AutoRescanAfterIsolate;
    }

    [ObservableProperty]
    private bool _showDuplicateCount = true;

    [ObservableProperty]
    private bool _showUniquePageCount = true;

    [ObservableProperty]
    private bool _showDuplicatePages = true;

    [ObservableProperty]
    private bool _matchFileName = true;

    [ObservableProperty]
    private bool _matchPageCount = true;

    [ObservableProperty]
    private bool _matchFileSize = true;

    [ObservableProperty]
    private bool _autoRescanAfterIsolate = true;

    partial void OnShowDuplicateCountChanged(bool value) => Save();

    partial void OnShowUniquePageCountChanged(bool value) => Save();

    partial void OnShowDuplicatePagesChanged(bool value) => Save();

    partial void OnMatchFileNameChanged(bool value) => Save();

    partial void OnMatchPageCountChanged(bool value) => Save();

    partial void OnMatchFileSizeChanged(bool value) => Save();

    partial void OnAutoRescanAfterIsolateChanged(bool value) => Save();

    private void Save()
    {
        var settings = _store.Load();
        settings.ShowDuplicateCount = ShowDuplicateCount;
        settings.ShowUniquePageCount = ShowUniquePageCount;
        settings.ShowDuplicatePages = ShowDuplicatePages;
        settings.MatchFileName = MatchFileName;
        settings.MatchPageCount = MatchPageCount;
        settings.MatchFileSize = MatchFileSize;
        settings.AutoRescanAfterIsolate = AutoRescanAfterIsolate;
        _store.Save(settings);
    }
}