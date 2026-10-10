using Microsoft.UI.Xaml;

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
    }

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        _store.Save(new AppSettings { Theme = theme.ToString() });
        Changed?.Invoke();
    }
}
