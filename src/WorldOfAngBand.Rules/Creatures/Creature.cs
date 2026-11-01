using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Creatures;

/// <summary>How tough a monster is for its zone. Each rank multiplies HP and shifts the effective level.</summary>
public enum CreatureRank
{
    Minion,
    Normal,
    Champion,
    Elite,
    Herald,
    Prophet,
}

/// <summary>
/// How a rank is scaled for one hero. Pathfinder's numbers assume a party of four, so the pack ranks hit
/// less often and less hard; elites and bosses keep the full values.
/// </summary>
public readonly record struct RankScaling(double HpMultiplier, int LevelOffset, int AttackOffset, double DamageMultiplier);

/// <summary>A creature's numbers at its effective level (zone level + its rank's level offset).</summary>
public sealed record CreatureStats(
    int Level,
    CreatureRank Rank,
    int EffectiveLevel,
    int ArmorClass,
    int AttackBonus,
    int SaveDc,
    int HitPoints,
    double DamageMultiplier)
{
    public static RankScaling ScalingFor(CreatureRank rank) => rank switch
    {
        CreatureRank.Minion => new(0.12, -2, -3, 0.35),
        CreatureRank.Normal => new(0.25, 0, -2, 0.4),
        CreatureRank.Champion => new(0.6, 1, -2, 0.5),
        CreatureRank.Elite => new(1.0, 2, -2, 0.6),
        CreatureRank.Herald => new(20.0, 2, 0, 1.0),
        CreatureRank.Prophet => new(35.0, 3, 0, 1.0),
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, null),
    };

    /// <summary>
    /// The spec's approximate creature-building formulas, in the shape of Pathfinder's "moderate" values:
    /// AC ~ 15 + L, attack ~ 7 + 1.2 L, save DC ~ 16 + 1.1 L, base HP ~ 20 + 17 (L - 1). The rank then
    /// multiplies HP, adds its solo-play attack offset and sets the damage multiplier.
    /// </summary>
    public static CreatureStats For(int level, CreatureRank rank)
    {
        var scaling = ScalingFor(rank);
        var l = Math.Max(-1, level + scaling.LevelOffset);
        var baseHp = Math.Max(1, 20 + 17 * (l - 1));
        return new CreatureStats(level, rank, l,
            ArmorClass: 15 + l,
            AttackBonus: (int)Math.Round(7 + 1.2 * l, MidpointRounding.AwayFromZero) + scaling.AttackOffset,
            SaveDc: (int)Math.Round(16 + 1.1 * l, MidpointRounding.AwayFromZero),
            HitPoints: Math.Max(1, (int)Math.Round(baseHp * scaling.HpMultiplier, MidpointRounding.AwayFromZero)),
            DamageMultiplier: scaling.DamageMultiplier);
    }

    /// <summary>
    /// A default strike for the effective level, averaging about 4 + 1.5 L (Pathfinder's moderate strike damage):
    /// 1d4 at low levels, then d8s, one more die every 5 levels. Creature content can supply its own dice instead.
    /// </summary>
    public Dice DefaultStrikeDamage => DefaultStrikeDamageFor(EffectiveLevel);

    public static Dice DefaultStrikeDamageFor(int effectiveLevel)
    {
        var average = 4 + 1.5 * effectiveLevel;
        if (average < 5)
            return new Dice(1, 4, Math.Max(0, (int)Math.Round(average - 2.5, MidpointRounding.AwayFromZero)));
        var count = 1 + Math.Max(0, effectiveLevel) / 5;
        return new Dice(count, 8, Math.Max(0, (int)Math.Round(average - 4.5 * count, MidpointRounding.AwayFromZero)));
    }
}

/// <summary>A live monster: its stats, conditions, defences and one strike.</summary>
public sealed class Creature(
    string name,
    CreatureStats stats,
    Dice strikeDamage,
    DamageType strikeDamageType,
    DamageProfile? defenses = null,
    double attacksPerSecond = 1.0)
    : IDefender
{
    public string Name { get; } = name;
    public CreatureStats Stats { get; } = stats;
    public Dice StrikeDamage { get; } = strikeDamage;
    public DamageType StrikeDamageType { get; internal set; } = strikeDamageType;
    public double AttacksPerSecond { get; } = attacksPerSecond;
    public DamageProfile Defenses { get; } = defenses ?? new DamageProfile();
    public ConditionSet Conditions { get; } = new(isBoss: stats.Rank is CreatureRank.Herald or CreatureRank.Prophet);
    public PersistentDamageSet Persistent { get; } = new();
    public int HitPoints { get; private set; } = stats.HitPoints;
    public bool IsDead => HitPoints <= 0;

    public ModifierTotal ArmorClass() => ModifierStack.Combine(
        new[] { new Modifier("Base", Stats.ArmorClass) }
            .Concat(ArmorClassShift != 0 ? [new Modifier("Stance", ArmorClassShift)] : Array.Empty<Modifier>())
            .Concat(Conditions.PenaltiesFor(StatisticKind.ArmorClass, AttributeKind.Dexterity)));

    /// <summary>A shift to its AC from how it stands (a boss's shield wall, or its armour cast off).</summary>
    public int ArmorClassShift { get; set; }

    public ModifierTotal AttackModifier() => ModifierStack.Combine(
        new[] { new Modifier("Base", Stats.AttackBonus) }
            .Concat(Conditions.PenaltiesFor(StatisticKind.Attack, AttributeKind.Strength)));

    /// <summary>
    /// A saving throw's modifier: its save DC less 10 (Pathfinder's moderate save), 2 lower below Herald rank as the
    /// pack ranks' attacks are for one hero, with the conditions that apply.
    /// </summary>
    public ModifierTotal Save(StatisticKind save) => ModifierStack.Combine(
        new[] { new Modifier("Base", Stats.SaveDc - 10 + (Stats.Rank < CreatureRank.Herald ? -2 : 0)) }
            .Concat(Conditions.PenaltiesFor(save, save switch
            {
                StatisticKind.Fortitude => AttributeKind.Constitution,
                StatisticKind.Reflex => AttributeKind.Dexterity,
                _ => AttributeKind.Wisdom,
            })));

    /// <summary>The DC to slip past its notice (a Stealth check): 10 + its Will save, which tracks Perception at its level.</summary>
    public int PerceptionDc => 10 + Save(StatisticKind.Will).Total;

    public void TakeDamage(int amount) => HitPoints = Math.Max(0, HitPoints - Math.Max(0, amount));

    /// <summary>Heals up to its maximum (an ally recovering).</summary>
    public void Heal(int amount) => HitPoints = Math.Min(Stats.HitPoints, HitPoints + Math.Max(0, amount));

    /// <summary>Sets its hit points outright, within 0 and its maximum (an ally restored from a save or rebuilt for a new level).</summary>
    public void SetHitPoints(int value) => HitPoints = Math.Clamp(value, 0, Stats.HitPoints);

    /// <summary>Back to full health, conditions and persistent damage gone (a boss resetting when the hero falls).</summary>
    public void Restore()
    {
        HitPoints = Stats.HitPoints;
        Persistent.Clear();
        Conditions.Clear();
    }
}
