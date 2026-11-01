using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Combat;

public enum DamageType
{
    Bludgeoning,
    Piercing,
    Slashing,
    Bleed,
    Fire,
    Cold,
    Electricity,
    Acid,
    Sonic,
    Force,
    Vitality,
    Void,
    Spirit,
    Mental,
    Poison,

    /// <summary>The setting's own damage type: it also fills the target's corruption meter.</summary>
    Taint,
}

/// <summary>
/// How an ability treats a failed attack: a plain miss, a graze for half damage, or (<see cref="Near"/>, the hero's
/// weapon Strikes) a graze only when the attack missed by <see cref="Strike.NearMissMargin"/> or less.
/// </summary>
public enum GrazeEffect
{
    None,
    Half,
    Near,
}

/// <summary>Immunities, resistances and weaknesses by damage type ("resistance 5 fire", "weakness 5 vitality").</summary>
public sealed class DamageProfile
{
    public static readonly DamageProfile None = new();

    public Dictionary<DamageType, int> Resistances { get; } = [];
    public Dictionary<DamageType, int> Weaknesses { get; } = [];
    public HashSet<DamageType> Immunities { get; } = [];

    public DamageProfile Resist(DamageType type, int amount)
    {
        Resistances[type] = Math.Max(amount, Resistances.GetValueOrDefault(type));
        return this;
    }

    public DamageProfile Weak(DamageType type, int amount)
    {
        Weaknesses[type] = Math.Max(amount, Weaknesses.GetValueOrDefault(type));
        return this;
    }

    public DamageProfile Immune(DamageType type)
    {
        Immunities.Add(type);
        return this;
    }
}

public static class DamageCalculator
{
    /// <summary>How much a type is held back by these defences: immunity most, then resistance less weakness.</summary>
    public static int Hinders(DamageType type, DamageProfile defenses) =>
        defenses.Immunities.Contains(type) ? 1000 : defenses.Resistances.GetValueOrDefault(type) - defenses.Weaknesses.GetValueOrDefault(type);

    /// <summary>Critical success doubles, success is normal, a failure is a miss or a half-damage graze, a critical failure deals nothing.</summary>
    public static double DegreeMultiplier(DegreeOfSuccess degree, GrazeEffect graze = GrazeEffect.None) => degree switch
    {
        DegreeOfSuccess.CriticalSuccess => 2.0,
        DegreeOfSuccess.Success => 1.0,
        DegreeOfSuccess.Failure => graze == GrazeEffect.Half ? 0.5 : 0.0,
        _ => 0.0,
    };

    /// <summary>
    /// Applies the target's defences in Pathfinder order: immunity first, then weakness and resistance,
    /// never going below zero. Weakness only applies when some damage gets through to apply it.
    /// </summary>
    public static int ApplyDefenses(int amount, DamageType type, DamageProfile? target)
    {
        target ??= DamageProfile.None;
        if (amount <= 0 || target.Immunities.Contains(type))
            return 0;
        var weakness = target.Weaknesses.GetValueOrDefault(type);
        var resistance = target.Resistances.GetValueOrDefault(type);
        return Math.Max(0, amount + weakness - resistance);
    }

    /// <summary>
    /// The spec's damage formula: (dice + flat bonuses) x degree multiplier, then resistance and weakness.
    /// A hit always deals at least 1 before the target's defences are applied.
    /// </summary>
    /// <summary>
    /// A basic saving throw (Pathfinder): the target's degree of success on its save sets the damage it takes from
    /// <paramref name="rolled"/>: none on a critical success, half on a success, all on a failure, double on a
    /// critical failure. Then its defences.
    /// </summary>
    public static int BasicSave(int rolled, DegreeOfSuccess save, DamageType type, DamageProfile? target)
    {
        var taken = save switch
        {
            DegreeOfSuccess.CriticalSuccess => 0,
            DegreeOfSuccess.Success => Math.Max(1, rolled) / 2,
            DegreeOfSuccess.Failure => Math.Max(1, rolled),
            _ => Math.Max(1, rolled) * 2,
        };
        return taken == 0 ? 0 : ApplyDefenses(taken, type, target);
    }

    public static int Compute(int diceTotal, int flatBonus, DegreeOfSuccess degree, DamageType type,
        DamageProfile? target, GrazeEffect graze = GrazeEffect.None, int extraCriticalDamage = 0)
    {
        var multiplier = DegreeMultiplier(degree, graze);
        if (multiplier == 0)
            return 0;
        var baseDamage = Math.Max(1, diceTotal + flatBonus);
        var dealt = (int)Math.Floor(baseDamage * multiplier);
        if (degree == DegreeOfSuccess.CriticalSuccess)
            dealt += extraCriticalDamage;
        return ApplyDefenses(Math.Max(1, dealt), type, target);
    }
}
