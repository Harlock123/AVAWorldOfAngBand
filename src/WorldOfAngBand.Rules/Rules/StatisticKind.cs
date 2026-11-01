namespace WorldOfAngBand.Rules.Rules;

/// <summary>The statistics conditions and gear can modify.</summary>
public enum StatisticKind
{
    ArmorClass,
    Attack,
    Damage,
    Fortitude,
    Reflex,
    Will,
    Perception,
    Skill,
    ClassDc,
    SpellAttack,
    SpellDc,
}

public static class StatisticKindExtensions
{
    public static bool IsSave(this StatisticKind kind) =>
        kind is StatisticKind.Fortitude or StatisticKind.Reflex or StatisticKind.Will;

    /// <summary>A DC rather than a roll. Frightened and sickened lower these too.</summary>
    public static bool IsDc(this StatisticKind kind) =>
        kind is StatisticKind.ClassDc or StatisticKind.SpellDc or StatisticKind.ArmorClass;
}
