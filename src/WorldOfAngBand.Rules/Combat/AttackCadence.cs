namespace WorldOfAngBand.Rules.Combat;

/// <summary>
/// The real-time multiple attack penalty. Plain attacks at the weapon's own rhythm take no penalty. An attack
/// that lands before the weapon's swing time has passed since the last one (an ability used mid-swing, a
/// dual-wield extra strike, a free attack from a feat) is chained: -2, then -4 (-1 and -2 with an agile weapon).
/// The chain ends once a full swing passes without a chained attack.
/// </summary>
public sealed class AttackCadence
{
    /// <summary>An attack within this fraction of a swing counts as early. The slack absorbs frame-timing jitter.</summary>
    public const double ChainFraction = 0.9;

    private double _lastAttackAt = double.NegativeInfinity;
    private int _chained;

    private bool IsEarly(double now, double swingSeconds) => now - _lastAttackAt < ChainFraction * swingSeconds;

    /// <summary>The penalty the next attack at <paramref name="now"/> would take, without recording it.</summary>
    public int PenaltyAt(double now, double swingSeconds, bool agile)
    {
        if (!IsEarly(now, swingSeconds))
            return 0;
        return _chained switch
        {
            0 => agile ? -1 : -2,
            _ => agile ? -2 : -4,
        };
    }

    /// <summary>Records an attack at <paramref name="now"/> and returns the penalty it took.</summary>
    public int Attack(double now, double swingSeconds, bool agile)
    {
        var penalty = PenaltyAt(now, swingSeconds, agile);
        _chained = IsEarly(now, swingSeconds) ? _chained + 1 : 0;
        _lastAttackAt = now;
        return penalty;
    }
}
