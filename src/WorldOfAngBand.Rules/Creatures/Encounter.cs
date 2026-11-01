namespace WorldOfAngBand.Rules.Creatures;

/// <summary>Threat levels and their XP budget for one pack, as in Pathfinder 2e.</summary>
public enum ThreatLevel
{
    Trivial = 40,
    Low = 60,
    Moderate = 80,
    Severe = 120,
    Extreme = 160,
}

public static class Encounter
{
    /// <summary>
    /// XP a creature is worth by its level against the player's, Pathfinder's table:
    /// -4: 10, -3: 15, -2: 20, -1: 30, 0: 40, +1: 60, +2: 80, +3: 120, +4 or more: 160. Lower than -4 is worth nothing.
    /// </summary>
    public static int CreatureXp(int creatureLevel, int playerLevel) => (creatureLevel - playerLevel) switch
    {
        < -4 => 0,
        -4 => 10,
        -3 => 15,
        -2 => 20,
        -1 => 30,
        0 => 40,
        1 => 60,
        2 => 80,
        3 => 120,
        _ => 160,
    };

    /// <summary>
    /// What one creature of a rank costs from a solo pack's budget, as a share of a full Pathfinder creature
    /// of the zone's level. Ranks are tuned for one hero (see <see cref="CreatureStats.ScalingFor"/>), so a
    /// Normal costs half. Bosses aren't built from pack budgets.
    /// </summary>
    public static double RankWeight(CreatureRank rank) => rank switch
    {
        CreatureRank.Minion => 0.25,
        CreatureRank.Normal => 0.5,
        CreatureRank.Champion => 1.0,
        CreatureRank.Elite => 2.0,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Bosses are tuned by hand, not by pack budget."),
    };

    public static int Budget(ThreatLevel threat) => (int)threat;

    /// <summary>The highest threat level a pack's total reaches (anything under the Low budget counts as Trivial).</summary>
    public static ThreatLevel Rate(int totalXp) => totalXp switch
    {
        >= (int)ThreatLevel.Extreme => ThreatLevel.Extreme,
        >= (int)ThreatLevel.Severe => ThreatLevel.Severe,
        >= (int)ThreatLevel.Moderate => ThreatLevel.Moderate,
        >= (int)ThreatLevel.Low => ThreatLevel.Low,
        _ => ThreatLevel.Trivial,
    };

    /// <summary>A pack's budget cost for a solo hero: each creature's XP at its zone level x its rank weight.</summary>
    public static int PackCost(IEnumerable<CreatureStats> pack, int playerLevel) =>
        (int)Math.Round(pack.Sum(c => CreatureXp(c.Level, playerLevel) * RankWeight(c.Rank)), MidpointRounding.AwayFromZero);
}
