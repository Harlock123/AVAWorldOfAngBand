using System.Text.Json;

namespace WorldOfAngBand.App.Services;

/// <summary>A hero who finished Ashen Reliquary: their name, the ending they reached (destroy, bind, claim, mercy), and when.</summary>
public sealed record LegacyHero(string Name, string Ending, DateTimeOffset SavedAt);

/// <summary>
/// Legacy (the spec's section 2.1): the endings reached in AVAAshenReliquary, read from its saves, so a new
/// Lantern-sworn can begin from the one the player made instead of a rumour. AshenReliquary keeps one JSON save per
/// character in its saves folder (<c>ASHEN_SAVE_DIR</c>, or the local application data folder's
/// <c>AshenReliquary/saves</c>); a finished game has its ending in <c>Flags["act5.ending"]</c>. Saves are only read.
/// </summary>
public static class AshenLegacy
{
    public const string EndingFlag = "act5.ending";

    public static string DefaultFolder =>
        Environment.GetEnvironmentVariable("ASHEN_SAVE_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AshenReliquary", "saves");

    /// <summary>The heroes in a saves folder who reached an ending, newest first; unreadable files are passed over.</summary>
    public static IReadOnlyList<LegacyHero> Read(string? folder = null)
    {
        folder ??= DefaultFolder;
        if (!Directory.Exists(folder))
            return [];
        var heroes = new List<LegacyHero>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                if (Get(root, "Flags") is not { ValueKind: JsonValueKind.Object } flags
                    || Get(flags, EndingFlag) is not { ValueKind: JsonValueKind.String } ending
                    || ending.GetString() is not ("destroy" or "bind" or "claim" or "mercy"))
                    continue;
                var name = Get(root, "Choices") is { ValueKind: JsonValueKind.Object } choices && Get(choices, "Name") is { ValueKind: JsonValueKind.String } n
                    ? n.GetString() ?? "The Ashbound"
                    : "The Ashbound";
                var saved = Get(root, "SavedAt") is { ValueKind: JsonValueKind.String } at && DateTimeOffset.TryParse(at.GetString(), out var when)
                    ? when
                    : new DateTimeOffset(File.GetLastWriteTimeUtc(file));
                heroes.Add(new LegacyHero(name, ending.GetString()!, saved));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }
        return heroes.OrderByDescending(h => h.SavedAt).ToList();
    }

    /// <summary>A property, its name matched without regard to case (AshenReliquary reads its saves that way).</summary>
    private static JsonElement? Get(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }
}
