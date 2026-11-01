using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Creatures;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game.Province;

/// <summary>A condition a creature's hit leaves, with the save that turns it aside.</summary>
public sealed record AshenInflict(ConditionKind Condition, int Value = 1, double Seconds = 0, string? Save = null);

/// <summary>
/// One of AshenReliquary's creatures, from its creatures.json (copied unchanged into data/province): how it hits (its
/// damage type and dice, how often, from how far), how it moves, what it resists and is weak to, and what its hits
/// leave. Its race in the game is "ashen_" + its id (tools/province/make_province_monsters.py).
/// </summary>
public sealed record AshenCreature(
    string Id,
    string Name,
    string Sprite,
    DamageType DamageType = DamageType.Bludgeoning,
    double AttacksPerSecond = 1,
    double MoveSpeed = 2.5,
    double AggroRadius = 8,
    double AttackRange = 1.3,
    bool Ranged = false,
    string? Damage = null,
    double HitPointsScale = 1,
    IReadOnlyDictionary<DamageType, int>? Weaknesses = null,
    IReadOnlyDictionary<DamageType, int>? Resistances = null,
    IReadOnlyList<DamageType>? Immunities = null,
    bool Undead = false,
    bool Caster = false,
    string? PoisonOnHit = null,
    double PoisonChance = 0,
    AshenInflict? Inflicts = null)
{
    public const string RacePrefix = "ashen_";

    /// <summary>The flag on their races: Angband's spawner never picks them.</summary>
    public const string ProvinceFlag = "PROVINCE";

    public string RaceId => RacePrefix + Id;

    /// <summary>Its defences, as AshenReliquary gives them (they are Pathfinder's numbers already).</summary>
    public DamageProfile Defenses()
    {
        var profile = new DamageProfile();
        foreach (var (type, amount) in Resistances ?? new Dictionary<DamageType, int>())
            profile.Resist(type, amount);
        foreach (var (type, amount) in Weaknesses ?? new Dictionary<DamageType, int>())
            profile.Weak(type, amount);
        foreach (var type in Immunities ?? [])
            profile.Immune(type);
        return profile;
    }

    /// <summary>
    /// Its speed on Angband's scale: AshenReliquary's creatures walk 2.5 tiles a second at a normal pace, and each half
    /// tile a second more or less is two and a half points of speed (a 3.0 is +2, a 1.5 is -5).
    /// </summary>
    public int Speed => (int)Math.Clamp(Math.Round((MoveSpeed - 2.5) * 5), -10, 10);
}

/// <summary>A creature as one of a pack in a zone: its numbers at the pack's level and rank, and which pack it is.</summary>
public sealed record AshenFoe(AshenCreature Creature, int Level, CreatureRank Rank, Dice Strike, double DamageMultiplier, int Pack)
{
    /// <summary>
    /// Its Pathfinder numbers (AshenReliquary's CreatureStats for the level and rank): AC, attack, saves and hit points
    /// (scaled as the creature says), and its strike (its own dice, else the default for its effective level).
    /// </summary>
    public static (AshenFoe Foe, FoeStats Stats, int HitPoints) For(AshenCreature creature, int level, CreatureRank rank, int pack)
    {
        var stats = CreatureStats.For(level, rank);
        var save = stats.SaveDc - 10 - (rank < CreatureRank.Herald ? 2 : 0);
        var foeStats = new FoeStats(level, rank, stats.ArmorClass, stats.AttackBonus, stats.SaveDc, save, save, save, save);
        var strike = creature.Damage is { } dice ? Dice.Parse(dice) : CreatureStats.DefaultStrikeDamageFor(stats.EffectiveLevel);
        var hp = Math.Max(1, (int)Math.Round(stats.HitPoints * creature.HitPointsScale));
        return (new AshenFoe(creature, level, rank, strike, stats.DamageMultiplier, pack), foeStats, hp);
    }
}
