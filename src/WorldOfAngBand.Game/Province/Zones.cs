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

/// <summary>One of a pack: which of AshenReliquary's creatures, at what rank, how many.</summary>
public sealed record PackMember(string Creature, Rules.Creatures.CreatureRank Rank = Rules.Creatures.CreatureRank.Normal, int Count = 1);

/// <summary>
/// A pack AshenReliquary placed: where, how far it spreads, its members, and the level it fights at (the zone's unless
/// it says). <see cref="Requires"/> and <see cref="Unless"/> are AshenReliquary's story conditions.
/// </summary>
public sealed record ZoneSpawn(int[] At, IReadOnlyList<PackMember> Pack, double Radius = 2.0, int? Level = null,
    System.Text.Json.JsonElement? Requires = null, System.Text.Json.JsonElement? Unless = null)
{
    /// <summary>
    /// Ten years on, AshenReliquary's Heralds and its Prophet are gone (their echoes are below), and the packs that
    /// hung on its story's turns stand down with them.
    /// </summary>
    public bool StandsTenYearsOn =>
        Requires is null && Unless is null && Pack.All(m => m.Rank < Rules.Creatures.CreatureRank.Herald);
}

/// <summary>A place in a zone: a shrine, a cache, an NPC or a keepsake.</summary>
public sealed record ZonePlace(string Id, int[] At, string? Name = null, int Facing = 0, string? Kind = null, string? Title = null,
    IReadOnlyDictionary<string, string>? Look = null, bool Still = false, string? Dialogue = null, string? Action = null);

/// <summary>A reading that carries something down to a story level: the flag it sets, and what the hero reads.</summary>
public sealed record TieIn(string Flag, string Value, string Text);

/// <summary>
/// This game's own notes on the province ten years on (data/province/province.json): who still stands where and what
/// they say ("zone/npc" ids), the readings that tie in to the story below, and the interactables whose turn in
/// AshenReliquary's story is over.
/// </summary>
public sealed record ProvinceNotes(
    IReadOnlyDictionary<string, string>? Npcs = null,
    IReadOnlyDictionary<string, TieIn>? TieIns = null,
    IReadOnlyList<string>? LeaveOut = null,
    IReadOnlyDictionary<string, ZoneSound>? Sound = null);

/// <summary>A zone's own music mood or ambience loop, in place of what its look gives it (the spec's section 8.1).</summary>
public sealed record ZoneSound(string? Music = null, string? Ambience = null);

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
    IReadOnlyList<ZonePlace>? Vendors = null,
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

    /// <summary>Whether a zone can be gone to: every zone of the province but Fort Gallowmere (which is Gallowmere itself).</summary>
    public bool IsOpen(string id) => id != Gallowmere && _zones.ContainsKey(id);

    private readonly Dictionary<string, ZoneDef> _zones;

    private readonly Dictionary<string, AshenCreature> _creatures;
    private readonly Dictionary<string, string> _readings;

    public ProvinceMap(IEnumerable<ZoneDef> zones, IEnumerable<AshenCreature>? creatures = null, ProvinceNotes? notes = null,
        IReadOnlyDictionary<string, string>? readings = null)
    {
        _zones = zones.ToDictionary(z => z.Id);
        _creatures = (creatures ?? []).ToDictionary(c => c.Id);
        Notes = notes ?? new ProvinceNotes();
        _readings = new Dictionary<string, string>(readings ?? new Dictionary<string, string>());
    }

    public ProvinceNotes Notes { get; }

    /// <summary>What an NPC says ten years on, or null when they are gone.</summary>
    public string? LineOf(string zoneId, string npcId) => Notes.Npcs?.GetValueOrDefault($"{zoneId}/{npcId}");

    /// <summary>A reading's tie-in to the story below, if it has one.</summary>
    public TieIn? TieInOf(string zoneId, string id) => Notes.TieIns?.GetValueOrDefault($"{zoneId}/{id}");

    /// <summary>What a note, grave or page says: the first words of its AshenReliquary dialogue.</summary>
    public string? Reading(string? dialogue) => dialogue is null ? null : _readings.GetValueOrDefault(dialogue);

    /// <summary>Whether an interactable is still there ten years on (not one whose turn in AshenReliquary's story is over).</summary>
    public bool Stands(ZonePlace place) =>
        Notes.LeaveOut is not { } gone || !(gone.Contains(place.Id) || place.Action is { } action && gone.Contains(action));

    /// <summary>One of AshenReliquary's creatures (data/province/creatures.json), by its id there.</summary>
    public AshenCreature? Creature(string id) => _creatures.GetValueOrDefault(id);

    public IReadOnlyCollection<AshenCreature> Creatures => _creatures.Values;

    public IReadOnlyCollection<ZoneDef> Zones => _zones.Values;

    public ZoneDef? this[string id] => _zones.GetValueOrDefault(id);

    /// <summary>Gallowmere's gates: the exits of Fort Gallowmere, each to an open zone.</summary>
    public IEnumerable<ZoneExit> Gates => this[Gallowmere]?.Exits.Where(e => e.To is { } to && IsOpen(to)) ?? [];

    private static readonly Lazy<ProvinceMap> SharedMap = new(() => Load());

    /// <summary>The province as shipped, loaded once.</summary>
    public static ProvinceMap Shared => SharedMap.Value;

    public static ProvinceMap Load(string? dataDirectory = null)
    {
        var root = Path.Combine(dataDirectory ?? DataLoader.DefaultDataDirectory, "province");
        var dir = Path.Combine(root, "zones");
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
        List<AshenCreature> creatures;
        using (var stream = File.OpenRead(Path.Combine(root, "creatures.json")))
            creatures = JsonSerializer.Deserialize<List<AshenCreature>>(stream, options) ?? [];
        ProvinceNotes notes;
        using (var stream = File.OpenRead(Path.Combine(root, "province.json")))
            notes = JsonSerializer.Deserialize<ProvinceNotes>(stream, options) ?? new ProvinceNotes();
        var readings = new Dictionary<string, string>();
        using (var stream = File.OpenRead(Path.Combine(root, "dialogues.json")))
        using (var doc = JsonDocument.Parse(stream))
        {
            foreach (var dialogue in doc.RootElement.EnumerateArray())
            {
                if (dialogue.TryGetProperty("id", out var id) && dialogue.TryGetProperty("start", out var start)
                    && dialogue.TryGetProperty("nodes", out var nodes) && nodes.TryGetProperty(start.GetString()!, out var node)
                    && node.TryGetProperty("text", out var text))
                    readings[id.GetString()!] = text.GetString()!;
            }
        }
        return new ProvinceMap(zones.ToList(), creatures, notes, readings);
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
