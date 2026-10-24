using System.Text.Json;

namespace Lux;

/// <summary>Einstellungen in %LOCALAPPDATA%\Lux\settings.json.</summary>
sealed class Settings
{
    static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lux", "settings.json");

    /// <summary>Alle Monitore bewegen sich zusammen.</summary>
    public bool Together { get; set; }

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (IOException)
        {
            // Nur eine Bequemlichkeit; ohne Datei startet LUX mit getrennten Reglern.
        }
    }
}
