using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PdfPageCounter_App;

/// <summary>
/// Converts a bool to Visibility (true = Visible, false = Collapsed).
/// WinUI 3 does not ship a built-in BooleanToVisibilityConverter, so the app
/// provides its own. A non-bool value falls back to Visible; null maps to Collapsed.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility.Visible;
    }
}