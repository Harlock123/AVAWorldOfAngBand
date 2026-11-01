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

    private static readonly Lazy<NamesFile> Names = new(() =>
    {
        var path = Path.Combine(DataLoader.DefaultDataDirectory, "setting", "ashen_names.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<NamesFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException($"{path} is empty.");
    });

    private sealed record NamesFile(Dictionary<string, string> Monsters, Dictionary<string, string> Egos, Dictionary<string, string> Objects,
        Dictionary<string, string>? Artifacts = null, List<string>? NameWords = null);

    /// <summary>A Gallowmere shop's name, by the shop id its entrance carries.</summary>
    public static string ShopName(string shopId) => Shops.GetValueOrDefault(shopId, shopId);

    /// <summary>A monster's name in the setting, or null when 4.2.5's name stands.</summary>
    public static string? MonsterName(string id) => Names.Value.Monsters.GetValueOrDefault(id);

    public static string? EgoName(string id) => Names.Value.Egos.GetValueOrDefault(id);

    /// <summary>An object kind's name in the setting (with Angband's ~ plural mark), or null.</summary>
    public static string? ObjectName(string id) => Names.Value.Objects.GetValueOrDefault(id);

    /// <summary>A standard artifact's name in the setting ('Ringil' is 'Coldflame'), or null when Angband's own stands.</summary>
    public static string? ArtifactName(string id) => Names.Value.Artifacts?.GetValueOrDefault(id);

    public static IReadOnlyDictionary<string, string> RenamedArtifacts => Names.Value.Artifacts ?? [];

    /// <summary>The setting's names, from which random artifacts' names are made (in place of Angband's Tolkien words).</summary>
    public static IReadOnlyList<string> NameWords => Names.Value.NameWords ?? [];

    public static IReadOnlyDictionary<string, string> RenamedMonsters => Names.Value.Monsters;
}
