using WorldOfAngBand.Rules.Combat;

namespace WorldOfAngBand.Game;

/// <summary>What else an element does to whoever it hurts, beyond its damage (the spec's section 6.2).</summary>
public enum Rider
{
    None,
    /// <summary>Nether, time: Drained 1 (hold life prevents it).</summary>
    Drain,
    /// <summary>Sound, water: Stunned for a moment.</summary>
    Stun,
    /// <summary>Shards: persistent bleeding.</summary>
    Bleed,
    /// <summary>Nexus: thrown a short way.</summary>
    Displace,
    /// <summary>Chaos: Stupefied (a clear mind prevents it).</summary>
    Confuse,
    /// <summary>Gravity, inertia: Slowed (free action prevents it).</summary>
    Slow,
    /// <summary>Light, darkness: Blinded (sight kept prevents it).</summary>
    Blind,
    /// <summary>Disenchantment: a worn item loses a point of its enchantment.</summary>
    Disenchant,
}

/// <summary>
/// Angband's 25 elements in Pathfinder's terms (the spec's section 6.2): each a damage type, and some an effect besides.
/// Pathfinder has no untyped damage, so what Angband leaves raw (chaos, time, mana, force) is force.
/// </summary>
public static class Elements
{
    private static readonly Dictionary<string, (DamageType Type, Rider Rider)> Table = new()
    {
        ["acid"] = (DamageType.Acid, Rider.None),
        ["elec"] = (DamageType.Electricity, Rider.None),
        ["fire"] = (DamageType.Fire, Rider.None),
        ["cold"] = (DamageType.Cold, Rider.None),
        ["pois"] = (DamageType.Poison, Rider.None),
        ["light"] = (DamageType.Vitality, Rider.Blind),
        ["dark"] = (DamageType.Void, Rider.Blind),
        ["sound"] = (DamageType.Sonic, Rider.Stun),
        ["shards"] = (DamageType.Slashing, Rider.Bleed),
        ["nexus"] = (DamageType.Force, Rider.Displace),
        ["nether"] = (DamageType.Void, Rider.Drain),
        ["chaos"] = (DamageType.Force, Rider.Confuse),
        ["disen"] = (DamageType.Force, Rider.Disenchant),
        ["water"] = (DamageType.Bludgeoning, Rider.Stun),
        ["ice"] = (DamageType.Cold, Rider.Bleed),
        ["gravity"] = (DamageType.Bludgeoning, Rider.Slow),
        ["inertia"] = (DamageType.Force, Rider.Slow),
        ["force"] = (DamageType.Force, Rider.Stun),
        ["time"] = (DamageType.Force, Rider.Drain),
        ["plasma"] = (DamageType.Fire, Rider.Stun),
        ["meteor"] = (DamageType.Fire, Rider.None),
        ["missile"] = (DamageType.Force, Rider.None),
        ["mana"] = (DamageType.Force, Rider.None),
        ["holy_orb"] = (DamageType.Spirit, Rider.None),
        ["arrow"] = (DamageType.Piercing, Rider.None),
    };

    /// <summary>The elements bridged (the bridge check holds every element in the data to this).</summary>
    public static IReadOnlyCollection<string> Known => Table.Keys;

    public static (DamageType Type, Rider Rider) For(string? element) =>
        element is not null && Table.TryGetValue(element, out var e) ? e : (DamageType.Force, Rider.None);
}
