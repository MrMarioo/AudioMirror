using System.Text.Json;
using AudioMirror.Infrastructure;

namespace AudioMirror.Settings;

/// <summary>Stores settings as JSON, by default in %AppData%\AudioMirror\settings.json.</summary>
internal sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioMirror", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return AppSettings.Default;

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            if (loaded is null) return AppSettings.Default;

            // Hand-edited or older files may lack the list entirely.
            return loaded with { SelectedDeviceIds = loaded.SelectedDeviceIds ?? [] };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Ignoring unreadable settings file {path}: {ex.Message}");
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write to a temp file and swap it in, so a crash mid-write can't leave a corrupt file.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save settings to {path}: {ex.Message}");
        }
    }
}
