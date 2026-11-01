using System.Text.Json;
using WorldOfAngBand.Data;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Creatures;

namespace WorldOfAngBand.Game.Bridge;

/// <summary>One Angband monster as data/bridge/monsters.json brings it into the game.</summary>
public sealed record BridgedMonster(string Id, string? Sprite = null, double Scale = 1, bool Tint = false, bool Ghost = false,
    IReadOnlyDictionary<string, string>? Doll = null);

/// <summary>A monster's Pathfinder numbers, worked out from its Angband entry (the spec's section 6.1).</summary>
public sealed record FoeStats(int Level, CreatureRank Rank, int ArmorClass, int AttackBonus, int SaveDc, int Fortitude, int Reflex,
    int Will, int Perception);

/// <summary>
/// The bridge from Angband's monsters to Pathfinder's creatures: which monsters are in the game, what they are called
/// in the Ashen setting, how they are drawn, and their level, AC, attack, saves and hit points. Level comes from depth
/// along a curve; AC, attack, saves and hit points come from AshenReliquary's creature formulas at that level, with AC
/// and hit points nudged by how the monster's Angband armour and hit points rank against others of its depth. Angband's
/// damage is scaled for depth and held to Pathfinder's for the level, and its speed above normal counts for half.
/// </summary>
public sealed class CreatureBridge
{
    /// <summary>Depth against creature level, between which levels are interpolated (the spec's section 4.3).</summary>
    private static readonly (int Depth, int Level)[] LevelCurve =
        [(0, -1), (1, -1), (4, 1), (10, 3), (20, 5), (30, 8), (60, 12), (90, 16), (100, 20), (127, 24)];

    private readonly Dictionary<string, BridgedMonster> _monsters;
    private readonly Dictionary<int, double> _armourAtDepth = [];
    private readonly double[] _hitPointsNear = new double[128];
    private readonly double[] _uniqueHitPointsNear = new double[128];

    public CreatureBridge(GameData data, IEnumerable<BridgedMonster> monsters)
    {
        _monsters = monsters.ToDictionary(m => m.Id);
        foreach (var m in _monsters.Keys.Where(id => data.Monster(id) is null))
            throw new GameDataException($"The bridge names unknown monster '{m}'.");
        foreach (var group in data.Monsters.GroupBy(m => m.Depth))
            _armourAtDepth[group.Key] = group.Average(m => m.Armour);
        // The middle of Angband's hit points round each depth (three levels either side), for common monsters and for
        // uniques, against which a monster's own are measured.
        Median(data.Monsters.Where(m => !m.IsUnique && m.HitPoints > 0).ToList(), 3, _hitPointsNear);
        Median(data.Monsters.Where(m => m.IsUnique && m.HitPoints > 1 && !m.Has(MonsterFlags.Questor)).ToList(), 6, _uniqueHitPointsNear);
        Data = data;
    }

    public GameData Data { get; }

    public IReadOnlyCollection<BridgedMonster> Monsters => _monsters.Values;

    /// <summary>
    /// The bridge as shipped, data/bridge/monsters.json beside the game data, with each mod's own bridge/monsters.json
    /// over it (an entry with the same id replaces the shipped one; a new id brings a modded monster into the game).
    /// </summary>
    public static CreatureBridge Load(GameData data, string? dataDirectory = null)
    {
        var monsters = new Dictionary<string, BridgedMonster>();
        foreach (var dir in new[] { dataDirectory ?? DataLoader.DefaultDataDirectory }.Concat(data.ModDirectories))
        {
            var path = Path.Combine(dir, "bridge", "monsters.json");
            if (!File.Exists(path))
                continue;
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<BridgeFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new GameDataException($"{path} is empty.");
            foreach (var m in file.Monsters)
                monsters[m.Id] = m;
        }
        return new CreatureBridge(data, monsters.Values);
    }

    private sealed record BridgeFile(List<BridgedMonster> Monsters);

    public bool Has(MonsterRaceDef race) => _monsters.ContainsKey(race.Id);

    public BridgedMonster? For(MonsterRaceDef race) => _monsters.GetValueOrDefault(race.Id);

    /// <summary>The monster's name in the Ashen setting.</summary>
    public string NameOf(MonsterRaceDef race) => Setting.MonsterName(race.Id) ?? race.Name;

    /// <summary>
    /// What Angband's monster damage is worth against a Pathfinder hero, by depth: a Pathfinder hero has fewer hit points
    /// than an Angband hero of the same depth (about 70 against 110 at depth 10, 300 against 900 at 90), so the damage
    /// shrinks with them: all of it to depth 5, 0.8 at 10, 0.55 at 30, 0.42 at 60, 0.35 at 90, 0.33 at 100. Past that,
    /// <see cref="SpellCap"/> and <see cref="BlowCap"/> hold it to Pathfinder's for the monster's level. The balance bots
    /// (Bots/Campaign) tune it.
    /// </summary>
    public static double DamageScale(int depth) => Curve(depth, [(5, 1.0), (10, 0.8), (20, 0.65), (30, 0.55), (60, 0.42), (90, 0.35), (100, 0.33)]);

    private static double Curve(int depth, (int Depth, double Scale)[] curve)
    {
        if (depth <= curve[0].Depth)
            return curve[0].Scale;
        for (var i = 1; i < curve.Length; i++)
        {
            if (depth > curve[i].Depth)
                continue;
            var (d0, s0) = curve[i - 1];
            var (d1, s1) = curve[i];
            return s0 + (s1 - s0) * (depth - d0) / (d1 - d0);
        }
        return curve[^1].Scale;
    }

    /// <summary>Scales an Angband amount of damage for a monster of <paramref name="depth"/>; never below 1.</summary>
    public static int Scaled(int amount, int depth) => amount <= 0 ? amount : Math.Max(1, (int)Math.Round(amount * DamageScale(depth)));

    /// <summary>
    /// The most a monster's spell or breath does at a creature level: Pathfinder's limited-use area damage, about a d6 a
    /// level and two more (24 at level 5, 63 at 16, 77 at 20, 91 at 24). A basic save still doubles it on a critical
    /// failure. A common monster does three quarters of that, as AshenReliquary's pack ranks hit a lone hero less hard
    /// than its elites.
    /// </summary>
    public static int SpellCap(int level, CreatureRank rank = CreatureRank.Elite) =>
        (int)Math.Round(3.5 * (Math.Max(0, level) + 2) * RankShare(rank));

    /// <summary>
    /// The most one of a monster's blows does at a creature level: about Pathfinder's extreme Strike damage (20 at 5,
    /// 48 at 16, 58 at 20); three quarters of that for a common monster.
    /// </summary>
    public static int BlowCap(int level, CreatureRank rank = CreatureRank.Elite) =>
        (int)Math.Round((2.5 * Math.Max(0, level) + 8) * RankShare(rank));

    private static double RankShare(CreatureRank rank) => rank < CreatureRank.Elite ? 0.75 : 1.0;

    /// <summary>
    /// A monster's speed in this game, relative to normal as the scheduler takes it: half of what Angband gives it above normal,
    /// since a Pathfinder hero's gear rarely brings the +20 an Angband hero has at the bottom (a +30 great wyrm is +15,
    /// two and a half turns to the hero's one, not four). Slow monsters stay as slow.
    /// </summary>
    public static int SpeedFor(int angbandSpeed) => angbandSpeed <= 0 ? angbandSpeed : angbandSpeed / 2;

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

    private static void Median(List<MonsterRaceDef> races, int spread, double[] into)
    {
        for (var depth = 0; depth < into.Length; depth++)
        {
            var near = races.Where(m => Math.Abs(m.Depth - depth) <= spread).Select(m => (double)m.HitPoints).Order().ToList();
            if (near.Count == 0)
                near = races.Select(m => (double)m.HitPoints).Order().ToList();
            into[depth] = near.Count == 0 ? 1 : Math.Max(1, near[near.Count / 2]);
        }
    }

    /// <summary>
    /// A monster's hit points (the spec's section 5.1): AshenReliquary's for its level and rank (a common monster has a
    /// quarter of Pathfinder's, as a lone hero's foes do there; a unique has all of them, two levels up), nudged by how
    /// its Angband hit points (<paramref name="angbandHitPoints"/>, as rolled) rank against others of its depth (uniques
    /// against uniques): the square root of the ratio, from 0.4 to 3. The story's keepers (Saint Ysmere, the Cinder)
    /// are measured against nobody: they have two and a half times a unique's.
    /// </summary>
    public int HitPointsFor(MonsterRaceDef race, int angbandHitPoints)
    {
        var stats = CreatureStats.For(LevelForDepth(race.Depth), race.IsUnique ? CreatureRank.Elite : CreatureRank.Normal);
        var depth = Math.Clamp(race.Depth, 0, _hitPointsNear.Length - 1);
        var ratio = angbandHitPoints / (race.IsUnique ? _uniqueHitPointsNear : _hitPointsNear)[depth];
        if (race.Has(MonsterFlags.Questor))
            return (int)Math.Round(stats.HitPoints * 2.5);
        return Math.Max(1, (int)Math.Round(stats.HitPoints * Math.Clamp(Math.Sqrt(ratio), 0.4, 3.0)));
    }

    /// <summary>
    /// An Angband amount measured in a monster's hit points (a breath's share, a heal) as the same share of this game's:
    /// a dragon at half its hit points breathes half as hard, here as there.
    /// </summary>
    public static int ShareOfHitPoints(int angbandAmount, MonsterRaceDef race, int maxHitPoints) =>
        angbandAmount <= 0 ? angbandAmount : Math.Max(1, (int)Math.Round((double)angbandAmount * maxHitPoints / Math.Max(1, race.HitPoints)));

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
