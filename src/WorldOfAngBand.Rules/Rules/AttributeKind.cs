namespace WorldOfAngBand.Rules.Rules;

/// <summary>The six Pathfinder attributes. Named <c>AttributeKind</c> so it never clashes with <see cref="System.Attribute"/>.</summary>
public enum AttributeKind
{
    Strength,
    Dexterity,
    Constitution,
    Intelligence,
    Wisdom,
    Charisma,
}

public static class AttributeKindExtensions
{
    public static string Abbreviation(this AttributeKind kind) => kind switch
    {
        AttributeKind.Strength => "STR",
        AttributeKind.Dexterity => "DEX",
        AttributeKind.Constitution => "CON",
        AttributeKind.Intelligence => "INT",
        AttributeKind.Wisdom => "WIS",
        AttributeKind.Charisma => "CHA",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>INT, WIS and CHA: the attributes the stupefied condition penalises.</summary>
    public static bool IsMental(this AttributeKind kind) =>
        kind is AttributeKind.Intelligence or AttributeKind.Wisdom or AttributeKind.Charisma;
}
