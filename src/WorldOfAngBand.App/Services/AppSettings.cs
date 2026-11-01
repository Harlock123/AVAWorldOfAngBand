using System.Text.Json;

namespace WorldOfAngBand.App.Services;

/// <summary>
/// The player's settings, kept in settings.json beside the Order's book: the mods switched off and the Sound window's
/// settings. A file that can't be read starts from the defaults.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(Graveyard.DefaultPath)!, "settings.json");

    /// <summary>Mods (by folder name) left out of the game.</summary>
    public List<string> DisabledMods { get; set; } = [];

    /// <summary>Volumes, switches and the mixing period (the spec's section 8.1).</summary>
    public Audio.SoundSettings Sound { get; set; } = new();

    public static AppSettings Read(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
