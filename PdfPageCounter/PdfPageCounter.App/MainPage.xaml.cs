using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PdfPageCounter.Core.Models;
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

        _viewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(_viewModel.Settings.ShowDuplicateCount) or
                                  nameof(_viewModel.Settings.ShowDuplicatePages) or
                                  nameof(_viewModel.Settings.ShowUniquePageCount))
            {
                UpdateCardColumnVisibilities();
            }
        };
        UpdateCardColumnVisibilities();
    }

    private void UpdateCardColumnVisibilities()
    {
        if (CardColDuplicateFiles != null)
            CardColDuplicateFiles.Width = _viewModel.Settings.ShowDuplicateCount ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (CardColDuplicatePages != null)
            CardColDuplicatePages.Width = _viewModel.Settings.ShowDuplicatePages ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (CardColUniquePages != null)
            CardColUniquePages.Width = _viewModel.Settings.ShowUniquePageCount ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    // Interactive Drag-to-Resize Splitter for Folder Pane
    private bool _isDraggingSplitter;
    private double _dragStartPointerX;
    private double _dragStartColumnWidth;

    private void FolderSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isDraggingSplitter = true;
            _dragStartPointerX = pt.Position.X;
            _dragStartColumnWidth = FolderColumn.ActualWidth;
            ((UIElement)sender).CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void FolderSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            var currentX = e.GetCurrentPoint(this).Position.X;
            var delta = currentX - _dragStartPointerX;
            var newWidth = Math.Clamp(_dragStartColumnWidth + delta, FolderColumn.MinWidth, FolderColumn.MaxWidth);
            FolderColumn.Width = new GridLength(newWidth);
            e.Handled = true;
        }
    }

    private void FolderSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            _isDraggingSplitter = false;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    private void FolderSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingSplitter = false;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } &&
            Enum.TryParse<AppTheme>(tag, out var theme))
        {
            App.ThemeManager.SetTheme(theme);
            if (App.MainAppWindow?.Content is FrameworkElement root)
            {
                App.ThemeManager.ApplyTo(root);
            }
        }
    }

    private async void IsolateDuplicatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanIsolate || _viewModel.IsolatableFileCount <= 0)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Isolate duplicates",
            Content = $"Move {_viewModel.IsolatableFileCount:N0} duplicate file(s) from {_viewModel.DuplicateGroupCount:N0} " +
                      $"group(s) into \"{_viewModel.CurrentIsolationRoot}\\duplicates\", keeping one original per group? " +
                      "You can revert this afterwards.",
            PrimaryButtonText = "Isolate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.IsolateDuplicatesCommand.ExecuteAsync(null);
        }
    }

    private async void RevertIsolationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanRevert)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Revert isolation",
            Content = "Move every isolated duplicate file out of the \"duplicates\" folder back to its original location?",
            PrimaryButtonText = "Revert",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.RevertIsolationCommand.ExecuteAsync(null);
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

    private void CopyPathsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PdfFileRecord record)
        {
            _viewModel.CopyGroupPaths(record);
        }
    }

    private async void BoxBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _viewModel.Boxes.BrowseSelected(folder.Path);
        }
    }

    private void BoxPathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && _viewModel.Boxes.ScanBoxesCommand.CanExecute(null))
        {
            _viewModel.Boxes.ScanBoxesCommand.Execute(null);
        }
    }

    private async void BoxExportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("Excel Workbook", new List<string> { ".xlsx" });
        picker.SuggestedFileName = $"BoxCounts-{DateTime.Now:yyyyMMdd-HHmm}";

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            await _viewModel.Boxes.ExportToAsync(file.Path);
        }
    }
}
