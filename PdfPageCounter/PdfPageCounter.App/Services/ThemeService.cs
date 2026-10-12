using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace PdfPageCounter_App.Services;

/// <summary>User-selectable application theme.</summary>
public enum AppTheme { System, Light, Dark }

/// <summary>
/// Owns the persisted <see cref="AppTheme"/> and applies it to a root element
/// (the window content), so the whole window — including the title bar and
/// flyouts — follows the choice.
/// </summary>
public sealed class ThemeService
{
    private readonly SettingsStore _store;

    public ThemeService(SettingsStore store)
    {
        _store = store;
        var saved = store.Load().Theme;
        Current = Enum.TryParse<AppTheme>(saved, ignoreCase: true, out var parsed)
            ? parsed
            : AppTheme.System;
    }

    public AppTheme Current { get; private set; }

    public event Action? Changed;

    public void ApplyTo(FrameworkElement root)
    {
        root.RequestedTheme = Current switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        ApplyCaptionButtons();
    }

    /// <summary>
    /// Colors the window caption buttons (minimize/maximize/close) so they stay
    /// visible in both light and dark themes. In System mode the defaults are
    /// cleared and Windows decides.
    /// </summary>
    public void ApplyCaptionButtons()
    {
        var titleBar = App.MainAppWindow?.AppWindow.TitleBar;
        if (titleBar is null)
        {
            return;
        }

        switch (Current)
        {
            case AppTheme.Light:
                SetCaptionColors(titleBar, dark: false);
                break;
            case AppTheme.Dark:
                SetCaptionColors(titleBar, dark: true);
                break;
            default:
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonPressedForegroundColor = null;
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                break;
        }
    }

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        _store.Save(new AppSettings { Theme = theme.ToString() });
        ApplyCaptionButtons();
        Changed?.Invoke();
    }

    private static void SetCaptionColors(AppWindowTitleBar titleBar, bool dark)
    {
        var glyph = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var hoverBg = dark ? Windows.UI.Color.FromArgb(255, 0x33, 0x33, 0x33) : Windows.UI.Color.FromArgb(255, 0xE5, 0xE5, 0xE5);
        var pressedBg = dark ? Windows.UI.Color.FromArgb(255, 0x44, 0x44, 0x44) : Windows.UI.Color.FromArgb(255, 0xCC, 0xCC, 0xCC);

        titleBar.ButtonForegroundColor = glyph;
        titleBar.ButtonHoverForegroundColor = glyph;
        titleBar.ButtonPressedForegroundColor = glyph;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = hoverBg;
        titleBar.ButtonPressedBackgroundColor = pressedBg;
    }
}

