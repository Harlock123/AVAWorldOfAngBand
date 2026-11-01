using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Items;

/// <summary>What an affix changes.</summary>
public enum AffixStat
{
    MaxHitPoints,
    MoveSpeed,
    AttackSpeed,
    CastSpeed,
    ResourceRegen,
    FuryPerHit,
    WeaponDamage,
    Resistance,
    Fortitude,
    Reflex,
    Will,
}

/// <summary>
/// A random affix from affixes.json. Values are untyped and small (the spec keeps runes as the main source of
/// power); <see cref="Min"/> and <see cref="Max"/> grow by <see cref="PerLevel"/> per item level. Percentages are
/// whole numbers here (5 = 5%).
/// </summary>
public sealed record AffixDefinition(
    string Id,
    string Text,
    bool Prefix,
    AffixStat Stat,
    double Min,
    double Max,
    double PerLevel = 0,
    IReadOnlyList<EquipSlot>? Slots = null,
    DamageType? Resist = null,
    int MinLevel = 0,
    bool MeleeOnly = false)
{
    public bool Fits(ItemDefinition item, int itemLevel) =>
        itemLevel >= MinLevel
        && item.Slot is { } slot
        && (Slots is null || Slots.Contains(slot) || (slot == EquipSlot.Ring2 && Slots.Contains(EquipSlot.Ring1)))
        && (!MeleeOnly || (item.IsWeapon && !item.CasterFocus));

    public bool IsPercent => Stat is AffixStat.MoveSpeed or AffixStat.AttackSpeed or AffixStat.CastSpeed or AffixStat.ResourceRegen;
}

/// <summary>One rolled affix on an item.</summary>
public sealed record AffixRoll(AffixDefinition Definition, int Value)
{
    public string Describe() => Definition.Stat switch
    {
        AffixStat.MaxHitPoints => $"+{Value} maximum HP",
        AffixStat.MoveSpeed => $"+{Value}% movement speed",
        AffixStat.AttackSpeed => $"+{Value}% attack speed",
        AffixStat.CastSpeed => $"+{Value}% cast speed",
        AffixStat.ResourceRegen => $"+{Value}% resource regeneration",
        AffixStat.FuryPerHit => $"+{Value} Fury per hit",
        AffixStat.WeaponDamage => $"+{Value} weapon damage",
        AffixStat.Resistance => $"Resistance {Value} {Definition.Resist?.ToString().ToLowerInvariant()}",
        AffixStat.Fortitude => $"+{Value} Fortitude",
        AffixStat.Reflex => $"+{Value} Reflex",
        AffixStat.Will => $"+{Value} Will",
        _ => Definition.Text,
    };
}
