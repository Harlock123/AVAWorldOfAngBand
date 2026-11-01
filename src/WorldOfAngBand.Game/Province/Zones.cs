using System.Text.Json;
using System.Text.Json.Serialization;
using WorldOfAngBand.Data;
using WorldOfAngBand.Engine.Definitions;

namespace WorldOfAngBand.Game.Province;

/// <summary>
/// One cell kind in a zone's legend, as AshenReliquary writes it (World/ZoneDefinition.cs): its ground (a name from
/// <see cref="ZoneBuilder.GroundSets"/>, or its own <see cref="Tiles"/>), what stands on it (tile ids in the zone's
/// tileset: one picked per cell, or all of them with <see cref="Stack"/>), whether it blocks walking and sight, and the
/// light it gives.
/// </summary>
public sealed record LegendEntry(string Ground, IReadOnlyList<int>? Objects = null, bool Blocks = false, bool BlocksSight = false,
    bool Stack = false, IReadOnlyList<int>? Tiles = null, double Light = 0, string? LightColor = null, bool Cover = false,
    bool Bridge = false, string? Crumbles = null);

/// <summary>A way out of a zone: where it is, how near counts, the zone it leads to and the exit there the hero comes out of.</summary>
public sealed record ZoneExit(string Id, string Label, int[] At, double Radius = 2.0, string? To = null, string? ToExit = null,
    int[]? Arrive = null);

/// <summary>A pack AshenReliquary placed (its creatures come into this game with M7b).</summary>
public sealed record ZoneSpawn(int[] At, double Radius = 2.0);

/// <summary>A place in a zone: a shrine, a cache, an NPC or a keepsake (M7b and M7c).</summary>
public sealed record ZonePlace(string Id, int[] At, string? Name = null);

/// <summary>
/// A zone of the province (the spec's section 7.4): AshenReliquary's zone file, read as it is
/// (data/province/zones, copied unchanged from AshenReliquary's Content/Data/zones).
/// </summary>
public sealed record ZoneDef(
    string Id,
    string Name,
    int Level,
    string Tileset,
    IReadOnlyList<string> Rows,
    IReadOnlyDictionary<string, LegendEntry> Legend,
    int[] PlayerStart,
    IReadOnlyList<ZoneExit> Exits,
    IReadOnlyList<ZoneSpawn>? Spawns = null,
    IReadOnlyList<ZonePlace>? Shrines = null,
    IReadOnlyList<ZonePlace>? Caches = null,
    IReadOnlyList<ZonePlace>? Npcs = null,
    IReadOnlyList<ZonePlace>? Interactables = null,
    bool Town = false,
    string? Tagline = null,
    double? Darkness = null,
    double HeroLight = 6.5,
    double XpScale = 1.0,
    int Act = 1,
    bool Cold = false,
    bool AshStorms = false)
{
    public int Width => Rows.Max(r => r.Length);

    public int Height => Rows.Count;

    /// <summary>AshenReliquary's darkness: 0.66 unless the zone says; from 0.85 a zone is dark and only its lights show it.</summary>
    public bool IsDark => (Darkness ?? 0.66) >= 0.85;
}

/// <summary>
/// The province: every zone, by id, and the depth each stands for. Gallowmere's wall has two gates, as Fort Gallowmere
/// had two exits: the north gate to the Gallows Road and the east gate to the Rotwater Fens.
/// </summary>
public sealed class ProvinceMap
{
    /// <summary>Fort Gallowmere's id in AshenReliquary's files: exits to it lead back to the town.</summary>
    public const string Gallowmere = "fort_gallowmere";

    /// <summary>The zones open in this milestone (M7a: the first two; the rest come with M7b).</summary>
    public static readonly IReadOnlySet<string> Open = new HashSet<string> { "gallows_road", "rotwater_fens" };

    private readonly Dictionary<string, ZoneDef> _zones;

    public ProvinceMap(IEnumerable<ZoneDef> zones) => _zones = zones.ToDictionary(z => z.Id);

    public IReadOnlyCollection<ZoneDef> Zones => _zones.Values;

    public ZoneDef? this[string id] => _zones.GetValueOrDefault(id);

    /// <summary>Gallowmere's gates: the exits of Fort Gallowmere, each to an open zone.</summary>
    public IEnumerable<ZoneExit> Gates => this[Gallowmere]?.Exits.Where(e => e.To is { } to && Open.Contains(to)) ?? [];

    private static readonly Lazy<ProvinceMap> SharedMap = new(() => Load());

    /// <summary>The province as shipped, loaded once.</summary>
    public static ProvinceMap Shared => SharedMap.Value;

    public static ProvinceMap Load(string? dataDirectory = null)
    {
        var dir = Path.Combine(dataDirectory ?? DataLoader.DefaultDataDirectory, "province", "zones");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var zones = Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal).Select(path =>
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<ZoneDef>(stream, options) ?? throw new GameDataException($"{path} is empty.");
        });
        return new ProvinceMap(zones);
    }

    /// <summary>
    /// The depth a zone's level stands for, for loot and for Angband's wanderers: where the spec expects a hero of that
    /// level to be (section 4.3: level 5 at depth 10, 10 at 30, 16 at 60, 20 at 90).
    /// </summary>
    public static int DepthFor(int level)
    {
        (int Level, int Depth)[] curve = [(1, 1), (5, 10), (10, 30), (16, 60), (20, 90)];
        if (level <= 1)
            return 1;
        for (var i = 1; i < curve.Length; i++)
        {
            if (level > curve[i].Level)
                continue;
            var (l0, d0) = curve[i - 1];
            var (l1, d1) = curve[i];
            return d0 + (d1 - d0) * (level - l0) / (l1 - l0);
        }
        return 90;
    }
}
