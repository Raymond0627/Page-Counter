using System.Text.Json;

namespace PdfPageCounter_App.Services;

/// <summary>Application settings persisted to disk.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
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
