namespace WorldOfAngBand.Rules.Items;

public enum ArmorCategory
{
    Unarmored,
    Light,
    Medium,
    Heavy,
}

/// <summary>
/// A base armour. Meeting <see cref="StrengthRequirement"/> removes the check penalty and cuts the speed
/// penalty by 5 ft; below it both apply in full.
/// </summary>
public sealed record ArmorDefinition(
    string Id,
    string Name,
    ArmorCategory Category,
    int AcBonus,
    int DexCap,
    int CheckPenalty = 0,
    int SpeedPenaltyFeet = 0,
    int StrengthRequirement = 0)
{
    /// <summary>No armour at all: +0 AC and no cap on DEX.</summary>
    public static readonly ArmorDefinition None = new("none", "Unarmored", ArmorCategory.Unarmored, 0, int.MaxValue);
}

/// <summary>Armour with its fundamental runes: potency (+1 to +3 AC) and resilient (+1 to +3 to saves).</summary>
public sealed record Armor(ArmorDefinition Definition, int Potency = 0, int Resilient = 0)
{
    public static readonly Armor None = new(ArmorDefinition.None);

    /// <summary>The armour's base AC and its potency rune together form one item bonus.</summary>
    public int ItemAcBonus => Definition.AcBonus + Potency;

    public int CheckPenalty(int strength) => strength >= Definition.StrengthRequirement ? 0 : Definition.CheckPenalty;

    public int SpeedPenaltyFeet(int strength) =>
        strength >= Definition.StrengthRequirement
            ? Math.Max(0, Definition.SpeedPenaltyFeet - 5)
            : Definition.SpeedPenaltyFeet;
}
