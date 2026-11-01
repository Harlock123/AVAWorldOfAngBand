using System.Text.Json;
using WorldOfAngBand.Data;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Creatures;

namespace WorldOfAngBand.Game.Bridge;

/// <summary>One Angband monster as data/bridge/monsters.json brings it into the game.</summary>
public sealed record BridgedMonster(string Id, string? Sprite = null, double Scale = 1, bool Tint = false, string? Name = null,
    IReadOnlyDictionary<string, string>? Doll = null);

/// <summary>A monster's Pathfinder numbers, worked out from its Angband entry (the spec's section 6.1).</summary>
public sealed record FoeStats(int Level, CreatureRank Rank, int ArmorClass, int AttackBonus, int SaveDc, int Fortitude, int Reflex,
    int Will, int Perception);

/// <summary>
/// The bridge from Angband's monsters to Pathfinder's creatures: which monsters are in the game, what they are called
/// in the Ashen setting, how they are drawn, and their level, AC, attack and saves. Hit points and speed stay
/// Angband's; level comes from depth along a curve; AC, attack and saves come from AshenReliquary's creature formulas
/// at that level, with AC nudged by how the monster's Angband armour ranks against others of its depth.
/// </summary>
public sealed class CreatureBridge
{
    /// <summary>Depth against creature level, between which levels are interpolated (the spec's section 4.3).</summary>
    private static readonly (int Depth, int Level)[] LevelCurve =
        [(0, -1), (1, -1), (4, 1), (10, 3), (20, 6), (30, 9), (60, 15), (90, 20), (100, 24), (127, 25)];

    private readonly Dictionary<string, BridgedMonster> _monsters;
    private readonly Dictionary<int, double> _armourAtDepth = [];

    public CreatureBridge(GameData data, IEnumerable<BridgedMonster> monsters)
    {
        _monsters = monsters.ToDictionary(m => m.Id);
        foreach (var m in _monsters.Keys.Where(id => data.Monster(id) is null))
            throw new GameDataException($"The bridge names unknown monster '{m}'.");
        foreach (var group in data.Monsters.GroupBy(m => m.Depth))
            _armourAtDepth[group.Key] = group.Average(m => m.Armour);
        Data = data;
    }

    public GameData Data { get; }

    public IReadOnlyCollection<BridgedMonster> Monsters => _monsters.Values;

    /// <summary>The bridge as shipped, data/bridge/monsters.json beside the game data.</summary>
    public static CreatureBridge Load(GameData data, string? dataDirectory = null)
    {
        var path = Path.Combine(dataDirectory ?? DataLoader.DefaultDataDirectory, "bridge", "monsters.json");
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<BridgeFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new GameDataException($"{path} is empty.");
        return new CreatureBridge(data, file.Monsters);
    }

    private sealed record BridgeFile(List<BridgedMonster> Monsters);

    public bool Has(MonsterRaceDef race) => _monsters.ContainsKey(race.Id);

    public BridgedMonster? For(MonsterRaceDef race) => _monsters.GetValueOrDefault(race.Id);

    /// <summary>The monster's name in the Ashen setting.</summary>
    public string NameOf(MonsterRaceDef race) => For(race)?.Name ?? race.Name;

    public static int LevelForDepth(int depth)
    {
        if (depth <= LevelCurve[0].Depth)
            return LevelCurve[0].Level;
        for (var i = 1; i < LevelCurve.Length; i++)
        {
            var (d1, l1) = LevelCurve[i];
            if (depth > d1)
                continue;
            var (d0, l0) = LevelCurve[i - 1];
            return (int)Math.Round(l0 + (l1 - l0) * (depth - d0) / (double)(d1 - d0), MidpointRounding.AwayFromZero);
        }
        return LevelCurve[^1].Level;
    }

    public FoeStats StatsFor(MonsterRaceDef race)
    {
        var rank = race.IsUnique ? CreatureRank.Elite : CreatureRank.Normal;
        var stats = CreatureStats.For(LevelForDepth(race.Depth), rank);
        var average = _armourAtDepth.GetValueOrDefault(race.Depth, race.Armour);
        var nudge = (int)Math.Clamp(Math.Round((race.Armour - average) / (average * 0.5 + 5)), -2, 2);
        // Pathfinder's moderate save is the save DC less 10; the pack ranks' are 2 lower, as AshenReliquary's are.
        var save = stats.SaveDc - 10 - (rank < CreatureRank.Herald ? 2 : 0);
        return new FoeStats(stats.Level, rank, stats.ArmorClass + nudge, stats.AttackBonus, stats.SaveDc,
            save, save, save, save);
    }

    /// <summary>Its resistances, weaknesses and immunities, from its Angband flags.</summary>
    public static DamageProfile DefensesFor(MonsterRaceDef race)
    {
        var profile = new DamageProfile();
        void Immune(string flag, DamageType type)
        {
            if (race.Has(flag))
                profile.Immune(type);
        }
        Immune("IM_ACID", DamageType.Acid);
        Immune("IM_ELEC", DamageType.Electricity);
        Immune("IM_FIRE", DamageType.Fire);
        Immune("IM_COLD", DamageType.Cold);
        Immune("IM_POIS", DamageType.Poison);
        var weakness = 2 + Math.Max(0, LevelForDepth(race.Depth)) / 2;
        if (race.Has("HURT_FIRE"))
            profile.Weak(DamageType.Fire, weakness);
        if (race.Has("HURT_COLD"))
            profile.Weak(DamageType.Cold, weakness);
        // Pathfinder's undead: vitality burns them, and poison has nothing to work on.
        if (race.Has(MonsterFlags.Undead))
        {
            profile.Weak(DamageType.Vitality, weakness);
            profile.Immune(DamageType.Poison);
        }
        return profile;
    }
}
