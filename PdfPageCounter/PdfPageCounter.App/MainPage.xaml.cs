using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PdfPageCounter_App.Services;
using PdfPageCounter_App.ViewModels;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace PdfPageCounter_App;

public sealed partial class MainPage : Page
{
    private readonly MainViewModel _viewModel;

    public MainPage()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(DispatcherQueue.GetForCurrentThread());
        DataContext = _viewModel;

        var current = App.ThemeManager.Current;
        ThemeSystem.IsChecked = current == AppTheme.System;
        ThemeLight.IsChecked = current == AppTheme.Light;
        ThemeDark.IsChecked = current == AppTheme.Dark;
    }

    private void ThemeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag } &&
            Enum.TryParse<AppTheme>(tag, out var theme))
        {
            App.ThemeManager.SetTheme(theme);
            if (App.MainAppWindow?.Content is FrameworkElement root)
            {
                App.ThemeManager.ApplyTo(root);
            }
        }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        // For unpackaged (non-MSIX) WinUI apps the picker needs a window handle.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _viewModel.BrowseSelected(folder.Path);
        }
    }

    private void PathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (_viewModel.ScanCommand.CanExecute(null))
            {
                _viewModel.ScanCommand.Execute(null);
            }
        }
    }

    private void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNodeViewModel vm)
        {
            _viewModel.SelectedFolder = vm;
        }
    }
}
