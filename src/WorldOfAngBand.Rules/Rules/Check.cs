namespace WorldOfAngBand.Rules.Rules;

public enum DegreeOfSuccess
{
    CriticalFailure,
    Failure,
    Success,
    CriticalSuccess,
}

/// <summary>One resolved d20 check.</summary>
public readonly record struct CheckResult(int Natural, int Modifier, int Dc, DegreeOfSuccess Degree)
{
    public int Total => Natural + Modifier;
}

public static class Check
{
    /// <summary>
    /// Pathfinder degrees of success: beat the DC by 10 for a critical success, miss it by 10 for a critical
    /// failure. A natural 20 then raises the result one step and a natural 1 lowers it one step.
    /// </summary>
    public static DegreeOfSuccess Degree(int total, int dc, int natural)
    {
        var degree = total >= dc + 10 ? DegreeOfSuccess.CriticalSuccess
            : total >= dc ? DegreeOfSuccess.Success
            : total <= dc - 10 ? DegreeOfSuccess.CriticalFailure
            : DegreeOfSuccess.Failure;
        return natural switch
        {
            20 => StepUp(degree),
            1 => StepDown(degree),
            _ => degree,
        };
    }

    public static CheckResult Roll(IRandomSource random, int modifier, int dc)
    {
        var natural = random.D20();
        return new CheckResult(natural, modifier, dc, Degree(natural + modifier, dc, natural));
    }

    public static DegreeOfSuccess StepUp(DegreeOfSuccess degree) =>
        degree == DegreeOfSuccess.CriticalSuccess ? degree : degree + 1;

    public static DegreeOfSuccess StepDown(DegreeOfSuccess degree) =>
        degree == DegreeOfSuccess.CriticalFailure ? degree : degree - 1;

    /// <summary>The chance (0 to 1) of reaching at least <paramref name="atLeast"/>; shown beside dialogue skill checks.</summary>
    public static double Chance(int modifier, int dc, DegreeOfSuccess atLeast = DegreeOfSuccess.Success)
    {
        var hits = 0;
        for (var natural = 1; natural <= 20; natural++)
        {
            if (Degree(natural + modifier, dc, natural) >= atLeast)
                hits++;
        }
        return hits / 20.0;
    }
}
