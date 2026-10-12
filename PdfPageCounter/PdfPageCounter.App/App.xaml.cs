using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using PdfPageCounter_App.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PdfPageCounter_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>The main application window, used for WinRT interop (e.g. folder pickers).</summary>
    public static Window? MainAppWindow { get; private set; }

    /// <summary>Application-wide theme manager.</summary>
    public static ThemeService ThemeManager { get; } = new(new SettingsStore());
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        UnhandledException += (s, e) =>
        {
            // Log so startup/resource failures are diagnosable, but do NOT mark
            // the exception as handled — WinUI must crash loudly rather than run
            // on without a window.
            Trace(e.Exception);
        };
        InitializeComponent();
    }

    private static void Trace(Exception ex)
    {
        try
        {
            var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ppc-crash.log");
            File.AppendAllText(log, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            MainAppWindow = _window;
            ThemeManager.ApplyCaptionButtons();
            _window.Activate();
        }
        catch (Exception ex)
        {
            Trace(ex);
            throw;
        }
    }
}
