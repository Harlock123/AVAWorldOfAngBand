using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Characters;

/// <summary>
/// Pathfinder's skills and the attribute each uses. Any "... Lore" skill uses Intelligence. Strength and Dexterity
/// skills take the armour's check penalty.
/// </summary>
public static class Skills
{
    public static readonly IReadOnlyDictionary<string, AttributeKind> Attribute = new Dictionary<string, AttributeKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["Acrobatics"] = AttributeKind.Dexterity,
        ["Arcana"] = AttributeKind.Intelligence,
        ["Athletics"] = AttributeKind.Strength,
        ["Crafting"] = AttributeKind.Intelligence,
        ["Deception"] = AttributeKind.Charisma,
        ["Diplomacy"] = AttributeKind.Charisma,
        ["Intimidation"] = AttributeKind.Charisma,
        ["Medicine"] = AttributeKind.Wisdom,
        ["Nature"] = AttributeKind.Wisdom,
        ["Occultism"] = AttributeKind.Intelligence,
        ["Performance"] = AttributeKind.Charisma,
        ["Religion"] = AttributeKind.Wisdom,
        ["Society"] = AttributeKind.Intelligence,
        ["Stealth"] = AttributeKind.Dexterity,
        ["Survival"] = AttributeKind.Wisdom,
        ["Thievery"] = AttributeKind.Dexterity,
    };

    public static bool IsLore(string skill) => skill.EndsWith(" Lore", StringComparison.OrdinalIgnoreCase);

    public static AttributeKind AttributeFor(string skill) =>
        IsLore(skill) ? AttributeKind.Intelligence
        : Attribute.TryGetValue(skill, out var attribute) ? attribute
        : throw new RulesException($"Unknown skill '{skill}'.");
}
