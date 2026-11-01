using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Items;

/// <summary>One character statistic as shown in an equip preview. Every statistic here is better when higher.</summary>
public sealed record StatValue(string Name, double Value, string Format, bool Core = true)
{
    public string Text => Value.ToString(Format, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A statistic before and after a change of gear.</summary>
public sealed record StatChange(string Name, StatValue Before, StatValue After)
{
    public double Delta => After.Value - Before.Value;

    /// <summary>+1 better, -1 worse, 0 the same (within rounding of what is displayed).</summary>
    public int Direction => Before.Text == After.Text ? 0 : Delta > 0 ? 1 : -1;
}

/// <summary>The character statistics an equip preview compares.</summary>
public static class StatSnapshot
{
    public static IReadOnlyList<StatValue> Take(Character c)
    {
        var w = c.Weapon;
        var averageHit = w.DiceCount * (w.Definition.DamageDie + 1) / 2.0 + c.DamageBonus().Total;
        var attacksPerSecond = w.Definition.AttacksPerSecond * (1 + Math.Min(c.Gear.AttackSpeed, ActionTiming.AttackSpeedCap));
        var caster = c.Class.Resource.StartsWith("Mana", StringComparison.OrdinalIgnoreCase);
        var stats = new List<StatValue>
        {
            new("Max HP", c.MaxHitPoints, "0"),
            new("AC", c.ArmorClass().Total, "0"),
        };
        if (caster)
        {
            stats.Add(new("Spell attack", c.SpellAttack().Total, "+0;-0;+0"));
            stats.Add(new("Cast speed", Math.Min(c.Gear.CastSpeed, ActionTiming.CastSpeedCap) * 100, "+0'%';-0'%';0'%'"));
        }
        stats.Add(new("Weapon attack", c.AttackModifier().Total, "+0;-0;+0"));
        stats.Add(new("Damage per hit", averageHit, "0.#"));
        stats.Add(new("Attacks per second", attacksPerSecond, "0.0#"));
        stats.Add(new("Weapon damage per second", averageHit * attacksPerSecond, "0.0"));
        stats.Add(new("Fortitude", c.Save(StatisticKind.Fortitude).Total, "+0;-0;+0"));
        stats.Add(new("Reflex", c.Save(StatisticKind.Reflex).Total, "+0;-0;+0"));
        stats.Add(new("Will", c.Save(StatisticKind.Will).Total, "+0;-0;+0"));
        stats.Add(new("Speed", c.MoveSpeedTilesPerSecond() / ActionTiming.MoveSpeedTilesPerSecond(25) * 100, "0'%'"));
        // Secondary stats only appear in a preview when they change.
        stats.Add(new("Resource regeneration", c.Gear.ResourceRegen * 100, "+0'%';-0'%';0'%'", Core: false));
        stats.Add(new("Fury per hit", c.Gear.FuryPerHit, "+0;-0;0", Core: false));
        foreach (var type in Enum.GetValues<DamageType>())
            stats.Add(new($"{type} resistance", c.Defenses.Resistances.GetValueOrDefault(type), "0", Core: false));
        return stats;
    }

    public static IReadOnlyList<StatChange> Compare(IReadOnlyList<StatValue> before, IReadOnlyList<StatValue> after) =>
        before.Zip(after, (b, a) => new StatChange(b.Name, b, a))
            .Where(change => change.Before.Core || change.Direction != 0)
            .ToList();
}
