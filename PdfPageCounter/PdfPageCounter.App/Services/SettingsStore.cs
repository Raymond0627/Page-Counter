using System.Text.Json;

namespace PdfPageCounter_App.Services;

/// <summary>Application settings persisted to disk.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";

    /// <summary>Show the "Duplicate Files" summary card.</summary>
    public bool ShowDuplicateCount { get; set; }

    /// <summary>Show the "Unique Pages" summary card.</summary>
    public bool ShowUniquePageCount { get; set; }

    /// <summary>Show the "Duplicate Pages" summary card.</summary>
    public bool ShowDuplicatePages { get; set; }

    /// <summary>Match duplicates on page count + size only (ignore filename).</summary>
    public bool IgnoreFileName { get; set; }

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
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Best-effort persistence; never crash the app over settings.
        }
    }
}
