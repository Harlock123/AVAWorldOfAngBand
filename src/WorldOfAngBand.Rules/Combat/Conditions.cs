using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Combat;

public enum ConditionKind
{
    Frightened,
    Sickened,
    Clumsy,
    Enfeebled,
    Stupefied,
    Drained,
    Slowed,
    Stunned,
    OffGuard,
    Prone,
    Grabbed,
    Immobilized,
    Blinded,
    Dazzled,
}

/// <summary>One condition on a combatant. <see cref="Remaining"/> is null for conditions that last until removed.</summary>
public sealed class ActiveCondition(ConditionKind kind, int value, double? remaining)
{
    public ConditionKind Kind { get; } = kind;
    public int Value { get; internal set; } = value;
    public double? Remaining { get; internal set; } = remaining;
    internal double DecayTimer { get; set; }
}

/// <summary>
/// The conditions on one combatant, ticked in real time. Valued conditions keep the higher of the old and
/// new value; frightened drops by 1 every 2 s; stunned is capped at 0.5 to 2 s and leaves 3 s of immunity;
/// bosses halve each control effect that lands within 10 s of the last.
/// </summary>
public sealed class ConditionSet(bool isBoss = false)
{
    public const double FrightenedDecaySeconds = 2.0;
    public const double StunImmunitySeconds = 3.0;
    public const double MinStunSeconds = 0.5;
    public const double MaxStunSeconds = 2.0;
    public const double BossDiminishingWindowSeconds = 10.0;
    public const double SlowedTimePerValue = 0.25;
    public const int OffGuardAcPenalty = 2;
    public const int ConcealmentFlatCheckDc = 11;

    private readonly Dictionary<ConditionKind, ActiveCondition> _active = [];
    private double _stunImmuneUntil = double.NegativeInfinity;
    private double _lastControlAt = double.NegativeInfinity;
    private double _controlMultiplier = 1.0;

    public bool IsBoss { get; } = isBoss;

    /// <summary>Seconds since this set was created, advanced by <see cref="Update"/>.</summary>
    public double Time { get; private set; }

    public IReadOnlyCollection<ActiveCondition> Active => _active.Values;

    public static bool IsControl(ConditionKind kind) =>
        kind is ConditionKind.Stunned or ConditionKind.Slowed or ConditionKind.Immobilized
            or ConditionKind.Grabbed or ConditionKind.Prone;

    public bool Has(ConditionKind kind) => _active.ContainsKey(kind);

    public int Value(ConditionKind kind) => _active.TryGetValue(kind, out var c) ? c.Value : 0;

    /// <summary>Applies a condition and returns the duration it actually got (0 when it was resisted by immunity).</summary>
    public double Apply(ConditionKind kind, int value = 1, double? durationSeconds = null)
    {
        if (kind == ConditionKind.Stunned)
        {
            if (Time < _stunImmuneUntil)
                return 0;
            durationSeconds = Math.Clamp(durationSeconds ?? MinStunSeconds, MinStunSeconds, MaxStunSeconds);
        }

        if (IsBoss && IsControl(kind) && durationSeconds is { } d)
        {
            _controlMultiplier = Time - _lastControlAt <= BossDiminishingWindowSeconds ? _controlMultiplier * 0.5 : 1.0;
            _lastControlAt = Time;
            durationSeconds = d * _controlMultiplier;
        }

        if (_active.TryGetValue(kind, out var existing))
        {
            existing.Value = Math.Max(existing.Value, value);
            existing.Remaining = existing.Remaining is null || durationSeconds is null
                ? null
                : Math.Max(existing.Remaining.Value, durationSeconds.Value);
        }
        else
        {
            _active[kind] = new ActiveCondition(kind, value, durationSeconds);
        }
        return durationSeconds ?? double.PositiveInfinity;
    }

    /// <summary>Every condition gone at once (a boss resetting).</summary>
    public void Clear() => _active.Clear();

    public void Remove(ConditionKind kind)
    {
        if (_active.Remove(kind) && kind == ConditionKind.Stunned)
            _stunImmuneUntil = Time + StunImmunitySeconds;
    }

    /// <summary>Lowers a valued condition (a successful save against sickened, say), removing it at 0.</summary>
    public void Reduce(ConditionKind kind, int by = 1)
    {
        if (!_active.TryGetValue(kind, out var c))
            return;
        c.Value -= by;
        if (c.Value <= 0)
            Remove(kind);
    }

    public void Update(double deltaSeconds)
    {
        Time += deltaSeconds;
        foreach (var c in _active.Values.ToList())
        {
            if (c.Kind == ConditionKind.Frightened)
            {
                c.DecayTimer += deltaSeconds;
                while (c.DecayTimer >= FrightenedDecaySeconds && c.Value > 0)
                {
                    c.DecayTimer -= FrightenedDecaySeconds;
                    c.Value--;
                }
                if (c.Value <= 0)
                {
                    Remove(c.Kind);
                    continue;
                }
            }

            if (c.Remaining is { } left)
            {
                c.Remaining = left - deltaSeconds;
                if (c.Remaining <= 0)
                    Remove(c.Kind);
            }
        }
    }

    // ----- What the conditions do ---------------------------------------------

    public bool CanAct => !Has(ConditionKind.Stunned);

    public bool CanMove => CanAct && !Has(ConditionKind.Prone) && !Has(ConditionKind.Grabbed) && !Has(ConditionKind.Immobilized);

    public bool IsOffGuard => Has(ConditionKind.OffGuard) || Has(ConditionKind.Prone) || Has(ConditionKind.Grabbed);

    /// <summary>Action and cast times are multiplied by this: +25% per point of slowed.</summary>
    public double ActionTimeMultiplier => 1.0 + SlowedTimePerValue * Value(ConditionKind.Slowed);

    /// <summary>Sickened creatures can't drink potions.</summary>
    public bool PotionsBlocked => Has(ConditionKind.Sickened);

    /// <summary>Blinded or dazzled cuts the light radius to 1 tile and makes attacks pass a DC 11 flat check first.</summary>
    public bool HasImpairedVision => Has(ConditionKind.Blinded) || Has(ConditionKind.Dazzled);

    /// <summary>Drained N removes N x level hit points from the maximum.</summary>
    public int MaxHitPointLoss(int level) => Value(ConditionKind.Drained) * level;

    /// <summary>The condition penalties on one statistic, governed by <paramref name="attribute"/>.</summary>
    public IEnumerable<Modifier> PenaltiesFor(StatisticKind statistic, AttributeKind? attribute)
    {
        var frightened = Value(ConditionKind.Frightened);
        if (frightened > 0 && statistic != StatisticKind.Damage)
            yield return new Modifier($"Frightened {frightened}", -frightened, BonusType.Status);

        var sickened = Value(ConditionKind.Sickened);
        if (sickened > 0 && statistic != StatisticKind.Damage)
            yield return new Modifier($"Sickened {sickened}", -sickened, BonusType.Status);

        var clumsy = Value(ConditionKind.Clumsy);
        if (clumsy > 0 && attribute == AttributeKind.Dexterity && statistic != StatisticKind.Damage)
            yield return new Modifier($"Clumsy {clumsy}", -clumsy, BonusType.Status);

        var enfeebled = Value(ConditionKind.Enfeebled);
        if (enfeebled > 0 && attribute == AttributeKind.Strength)
            yield return new Modifier($"Enfeebled {enfeebled}", -enfeebled, BonusType.Status);

        var stupefied = Value(ConditionKind.Stupefied);
        if (stupefied > 0 && attribute?.IsMental() == true && statistic != StatisticKind.Damage)
            yield return new Modifier($"Stupefied {stupefied}", -stupefied, BonusType.Status);

        var drained = Value(ConditionKind.Drained);
        if (drained > 0 && statistic == StatisticKind.Fortitude)
            yield return new Modifier($"Drained {drained}", -drained, BonusType.Status);

        if (statistic == StatisticKind.ArmorClass && IsOffGuard)
            yield return new Modifier("Off-guard", -OffGuardAcPenalty, BonusType.Circumstance);
    }
}
