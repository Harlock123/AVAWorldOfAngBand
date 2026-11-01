using System.Text.Json;
using WorldOfAngBand.Data;

namespace WorldOfAngBand.Game;

/// <summary>
/// Names in the Ashen setting for what Angband 4.2.5 names after Tolkien (the spec's section 3): monsters, egos,
/// objects and artifacts, from data/setting/ashen_names.json, and Gallowmere's shops. The 4.2.5 data keeps its own names; the game shows these.
/// </summary>
public static class Setting
{
    private static readonly Dictionary<string, string> Shops = new()
    {
        ["general"] = "Quartermaster",
        ["armoury"] = "Armourer",
        ["weaponsmith"] = "Smith",
        ["bookseller"] = "The Lantern Archive",
        ["alchemist"] = "Apothecary",
        ["magic"] = "Curio Dealer",
        ["black_market"] = "The Hanged Man",
        ["home"] = "Your Room at the Inn",
        ["inn"] = "The Lantern Inn",
        ["caravan"] = "Caravan",
        ["artificer"] = "Artificer",
    };

    private static Lazy<NamesFile> Names = new(() => Read([]));

    private static IReadOnlyList<string> _modDirectories = [];

    /// <summary>
    /// The mods in play: each one's ashen_names.json goes over the setting's own names (a name with the same id replaces
    /// the shipped one). The names are read again only when the mods change.
    /// </summary>
    public static void UseMods(IReadOnlyList<string> modDirectories)
    {
        if (modDirectories.SequenceEqual(_modDirectories))
            return;
        _modDirectories = modDirectories.ToList();
        var dirs = _modDirectories;
        Names = new Lazy<NamesFile>(() => Read(dirs));
    }

    private static NamesFile Read(IReadOnlyList<string> modDirectories)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var path = Path.Combine(DataLoader.DefaultDataDirectory, "setting", "ashen_names.json");
        NamesFile names;
        using (var stream = File.OpenRead(path))
            names = JsonSerializer.Deserialize<NamesFile>(stream, options) ?? throw new InvalidDataException($"{path} is empty.");
        foreach (var dir in modDirectories)
        {
            var mod = Path.Combine(dir, "ashen_names.json");
            if (Path.GetFullPath(mod) == Path.GetFullPath(path) || !File.Exists(mod))
                continue;
            using var stream = File.OpenRead(mod);
            if (JsonSerializer.Deserialize<NamesFile>(stream, options) is not { } over)
                continue;
            names = new NamesFile(Over(names.Monsters, over.Monsters), Over(names.Egos, over.Egos), Over(names.Objects, over.Objects),
                Over(names.Artifacts, over.Artifacts), names.NameWords);
        }
        return names;
    }

    /// <summary>The monster names with these mods over the setting's (tests).</summary>
    internal static IReadOnlyDictionary<string, string> MonsterNamesWith(IReadOnlyList<string> modDirectories) => Read(modDirectories).Monsters ?? [];

    private static Dictionary<string, string> Over(Dictionary<string, string>? under, Dictionary<string, string>? over)
    {
        var merged = new Dictionary<string, string>(under ?? []);
        foreach (var (id, name) in over ?? [])
            merged[id] = name;
        return merged;
    }

    private sealed record NamesFile(Dictionary<string, string>? Monsters, Dictionary<string, string>? Egos, Dictionary<string, string>? Objects,
        Dictionary<string, string>? Artifacts = null, List<string>? NameWords = null);

    /// <summary>A Gallowmere shop's name, by the shop id its entrance carries.</summary>
    public static string ShopName(string shopId) => Shops.GetValueOrDefault(shopId, shopId);

    /// <summary>A monster's name in the setting, or null when 4.2.5's name stands.</summary>
    public static string? MonsterName(string id) => Names.Value.Monsters?.GetValueOrDefault(id);

    public static string? EgoName(string id) => Names.Value.Egos?.GetValueOrDefault(id);

    /// <summary>An object kind's name in the setting (with Angband's ~ plural mark), or null.</summary>
    public static string? ObjectName(string id) => Names.Value.Objects?.GetValueOrDefault(id);

    /// <summary>A standard artifact's name in the setting ('Ringil' is 'Coldflame'), or null when Angband's own stands.</summary>
    public static string? ArtifactName(string id) => Names.Value.Artifacts?.GetValueOrDefault(id);

    public static IReadOnlyDictionary<string, string> RenamedArtifacts => Names.Value.Artifacts ?? [];

    /// <summary>The setting's names, from which random artifacts' names are made (in place of Angband's Tolkien words).</summary>
    public static IReadOnlyList<string> NameWords => Names.Value.NameWords ?? [];

    public static IReadOnlyDictionary<string, string> RenamedMonsters => Names.Value.Monsters ?? [];
}
