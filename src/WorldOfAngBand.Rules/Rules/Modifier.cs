namespace WorldOfAngBand.Rules.Rules;

/// <summary>The three typed bonuses plus untyped. Only the best bonus and the worst penalty of each typed kind apply.</summary>
public enum BonusType
{
    Untyped,
    Item,
    Status,
    Circumstance,
}

/// <summary>One named contribution to a statistic, e.g. "Expert" +9 or "Frightened 2" -2 (status).</summary>
public readonly record struct Modifier(string Source, int Value, BonusType Type = BonusType.Untyped)
{
    public bool IsPenalty => Value < 0;

    public override string ToString()
    {
        var type = Type == BonusType.Untyped ? "" : $" {Type.ToString().ToLowerInvariant()}";
        return $"{Source} {Value:+0;-0;+0}{type}";
    }
}

/// <summary>The modifiers that survived the stacking rule, their total, and what was dropped. Drives the formula tooltips.</summary>
public sealed record ModifierTotal(int Total, IReadOnlyList<Modifier> Applied, IReadOnlyList<Modifier> Suppressed);

public static class ModifierStack
{
    /// <summary>
    /// Pathfinder bonus stacking: for each typed kind only the highest bonus and the lowest penalty count;
    /// untyped bonuses and penalties all add up. Zero-value modifiers are kept out of the result.
    /// </summary>
    public static ModifierTotal Combine(IEnumerable<Modifier> modifiers)
    {
        var applied = new List<Modifier>();
        var suppressed = new List<Modifier>();
        foreach (var group in modifiers.Where(m => m.Value != 0).GroupBy(m => m.Type))
        {
            if (group.Key == BonusType.Untyped)
            {
                applied.AddRange(group);
                continue;
            }
            Keep(group.Where(m => !m.IsPenalty).OrderByDescending(m => m.Value).ToList());
            Keep(group.Where(m => m.IsPenalty).OrderBy(m => m.Value).ToList());
        }
        return new ModifierTotal(applied.Sum(m => m.Value), applied, suppressed);

        void Keep(List<Modifier> ordered)
        {
            if (ordered.Count == 0)
                return;
            applied.Add(ordered[0]);
            suppressed.AddRange(ordered.Skip(1));
        }
    }

    public static int Total(IEnumerable<Modifier> modifiers) => Combine(modifiers).Total;
}
