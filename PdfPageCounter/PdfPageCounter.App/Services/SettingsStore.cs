using System.Text.Json;

namespace PdfPageCounter_App.Services;

/// <summary>Application settings persisted to disk.</summary>
public sealed class AppSettings
{
    /// <summary>Schema version; used to run one-time migrations. 0 = pre-migration.</summary>
    public int SettingsVersion { get; set; }

    public string Theme { get; set; } = "System";

    /// <summary>Show the "Duplicate Files" summary card.</summary>
    public bool ShowDuplicateCount { get; set; } = true;

    /// <summary>Show the "Unique Pages" summary card.</summary>
    public bool ShowUniquePageCount { get; set; } = true;

    /// <summary>Show the "Duplicate Pages" summary card.</summary>
    public bool ShowDuplicatePages { get; set; } = true;

    /// <summary>Match duplicates on file name.</summary>
    public bool MatchFileName { get; set; } = true;

    /// <summary>Match duplicates on page count.</summary>
    public bool MatchPageCount { get; set; } = true;

    /// <summary>Match duplicates on file size.</summary>
    public bool MatchFileSize { get; set; } = true;

    /// <summary>Re-scan the same root automatically after isolating or reverting.</summary>
    public bool AutoRescanAfterIsolate { get; set; } = true;
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under
/// <c>%LOCALAPPDATA%\LumeedPcount\settings.json</c>. All IO failures are
/// swallowed so a missing or corrupt file can never prevent the app from
/// starting.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>Current settings schema version. Bump when a migration is added.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumeedPcount", "settings.json");

    private readonly string _filePath;

    public SettingsStore(string? filePath = null) => _filePath = filePath ?? DefaultPath;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions)
                       ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            // Every save stamps the current schema version, so migrations run once.
            settings.SettingsVersion = CurrentVersion;
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Best-effort persistence; never crash the app over settings.
        }
    }

    /// <summary>
    /// One-time migration. Settings written by an older build (SettingsVersion &lt; 1)
    /// get the three optional summary cards turned back on so all cards show after
    /// this update. Runs at most once; fresh installs (no file) are unaffected
    /// because their defaults are already all-on.
    /// </summary>
    public void Migrate()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                // No saved settings yet: nothing to migrate (defaults are all-on).
                return;
            }

            var settings = Load();
            if (settings.SettingsVersion >= CurrentVersion)
            {
                return;
            }

            settings.ShowDuplicateCount = true;
            settings.ShowUniquePageCount = true;
            settings.ShowDuplicatePages = true;
            settings.SettingsVersion = CurrentVersion;
            Save(settings);
        }
        catch
        {
            // Never block startup over a migration.
        }
    }
}
