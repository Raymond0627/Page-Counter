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
        AutoRescanAfterIsolate = saved.AutoRescanAfterIsolate;
    }

    [ObservableProperty]
    private bool _showDuplicateCount;

    [ObservableProperty]
    private bool _showUniquePageCount;

    [ObservableProperty]
    private bool _showDuplicatePages;

    [ObservableProperty]
    private bool _autoRescanAfterIsolate = true;

    partial void OnShowDuplicateCountChanged(bool value) => Save();

    partial void OnShowUniquePageCountChanged(bool value) => Save();

    partial void OnShowDuplicatePagesChanged(bool value) => Save();

    partial void OnAutoRescanAfterIsolateChanged(bool value) => Save();

    private void Save()
    {
        var settings = _store.Load();
        settings.ShowDuplicateCount = ShowDuplicateCount;
        settings.ShowUniquePageCount = ShowUniquePageCount;
        settings.ShowDuplicatePages = ShowDuplicatePages;
        settings.AutoRescanAfterIsolate = AutoRescanAfterIsolate;
        _store.Save(settings);
    }
}