using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Items;

/// <summary>
/// Everything equipped items add beyond weapon and armour stats: the sum of their affixes and implicit bonuses.
/// Percentages are fractions (0.05 = 5%) and are capped later by <see cref="ActionTiming"/>.
/// </summary>
public sealed record GearBonuses
{
    public static readonly GearBonuses None = new();

    public int MaxHitPoints { get; init; }
    public double MoveSpeed { get; init; }
    public double AttackSpeed { get; init; }
    public double CastSpeed { get; init; }
    public double ResourceRegen { get; init; }
    public double FuryPerHit { get; init; }
    public int WeaponDamage { get; init; }
    public IReadOnlyDictionary<DamageType, int> Resistances { get; init; } = new Dictionary<DamageType, int>();
    public IReadOnlyDictionary<StatisticKind, int> Saves { get; init; } = new Dictionary<StatisticKind, int>();
}
