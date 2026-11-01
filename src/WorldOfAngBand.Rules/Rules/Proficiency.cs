namespace WorldOfAngBand.Rules.Rules;

public enum ProficiencyRank
{
    Untrained = 0,
    Trained = 1,
    Expert = 2,
    Master = 3,
    Legendary = 4,
}

public static class Proficiency
{
    /// <summary>Untrained +0; otherwise 2 per rank plus the character's level (trained 2 + level ... legendary 8 + level).</summary>
    public static int Bonus(ProficiencyRank rank, int level) =>
        rank == ProficiencyRank.Untrained ? 0 : 2 * (int)rank + level;

    /// <summary>The lowest level at which a skill increase may raise a skill to <paramref name="rank"/>.</summary>
    public static int MinimumLevelForSkillRank(ProficiencyRank rank) => rank switch
    {
        ProficiencyRank.Untrained or ProficiencyRank.Trained => 1,
        ProficiencyRank.Expert => 3,
        ProficiencyRank.Master => 7,
        ProficiencyRank.Legendary => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, null),
    };
}
