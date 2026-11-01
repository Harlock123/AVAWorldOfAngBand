namespace WorldOfAngBand.Rules.Progression;

public enum DifficultyMode
{
    Story,
    Veteran,
    Elite,
}

/// <summary>Multipliers for one difficulty mode. Hardcore is a separate flag that can be combined with any mode.</summary>
public sealed record DifficultySettings(
    DifficultyMode Mode,
    double EnemyHpMultiplier,
    double EnemyDamageMultiplier,
    int EnemyLevelOffset,
    double RewardMultiplier,
    double UniqueDropMultiplier,
    bool Hardcore = false)
{
    public const double HardcoreXpBonus = 0.10;

    public static DifficultySettings For(DifficultyMode mode, bool hardcore = false) => mode switch
    {
        DifficultyMode.Story => new(mode, 0.6, 0.6, 0, 1.0, 1.0, hardcore),
        DifficultyMode.Veteran => new(mode, 1.0, 1.0, 0, 1.0, 1.0, hardcore),
        DifficultyMode.Elite => new(mode, 1.6, 1.4, 2, 1.5, 2.0, hardcore),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public double XpMultiplier => Hardcore ? 1.0 + HardcoreXpBonus : 1.0;
}
