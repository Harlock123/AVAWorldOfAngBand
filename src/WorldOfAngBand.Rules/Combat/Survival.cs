using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Combat;

/// <summary>Hero Points: one per zone, at most three. Spent to reroll a check, or automatically to survive death.</summary>
/// <summary>
/// Second Breath, which takes the place of Hero Points (the spec's section 4.2: with permadeath a reroll is too strong
/// to give in every fight): at 0 HP, once per descent, the hero stays at 1 HP. Going back to town refreshes it.
/// </summary>
public sealed class SecondBreath
{
    public bool Ready { get; private set; } = true;

    public void Refresh() => Ready = true;

    internal void Restore(bool ready) => Ready = ready;

    public bool TrySpend()
    {
        if (!Ready)
            return false;
        Ready = false;
        return true;
    }
}

/// <summary>
/// Healing potions run on charges rather than a stack: 3 charges refilled at shrines, an 8 s cooldown,
/// and each drink heals 1d8 + 2 x level over 2 s. Sickened creatures can't drink. A draught picked up (or bought) with
/// the flasks full goes into the draught case, which holds <see cref="CaseCapacity"/> spares; out of combat a spare
/// refills an empty flask (<see cref="TopUp"/>), so a fight still has at most three drinks.
/// </summary>
public sealed class HealingPotion
{
    public const int MaxCharges = 3;
    public const double CooldownSeconds = 8.0;
    public const double HealDurationSeconds = 2.0;

    /// <summary>How many spare draughts the case holds.</summary>
    public const int CaseCapacity = 6;

    private double _readyAt = double.NegativeInfinity;

    public int Charges { get; private set; } = MaxCharges;

    /// <summary>Spare draughts in the case (shrines refill the flasks, never the case).</summary>
    public int Spares { get; private set; }

    public bool CanDrink(double now, ConditionSet? conditions = null) =>
        Charges > 0 && now >= _readyAt && conditions?.PotionsBlocked != true;

    /// <summary>Drinks a charge and returns the total healing, spread over <see cref="HealDurationSeconds"/>; null when it can't be drunk.</summary>
    public int? Drink(double now, int level, IRandomSource random, ConditionSet? conditions = null)
    {
        if (!CanDrink(now, conditions))
            return null;
        Charges--;
        _readyAt = now + CooldownSeconds;
        return random.Roll(1, 8) + 2 * level;
    }

    /// <summary>Seconds until the next drink is allowed (0 when ready), for the HUD's cooldown sweep.</summary>
    public double CooldownRemaining(double now) => Math.Max(0, _readyAt - now);

    public void Refill() => Charges = MaxCharges;

    public void Restore(int charges, int spares = 0)
    {
        Charges = Math.Clamp(charges, 0, MaxCharges);
        Spares = Math.Clamp(spares, 0, CaseCapacity);
    }

    /// <summary>True when the flasks and the case are both full: a draught has nowhere to go.</summary>
    public bool Full => Charges >= MaxCharges && Spares >= CaseCapacity;

    /// <summary>
    /// A healing draught picked up or bought: a flask if one is empty, else the case. False when both are full (it stays
    /// where it lies).
    /// </summary>
    public bool AddCharge()
    {
        if (Charges < MaxCharges)
            Charges++;
        else if (Spares < CaseCapacity)
            Spares++;
        else
            return false;
        return true;
    }

    /// <summary>Pours a spare from the case into an empty flask. False when there is no spare or no empty flask.</summary>
    public bool TopUp()
    {
        if (Spares == 0 || Charges >= MaxCharges)
            return false;
        Spares--;
        Charges++;
        return true;
    }
}

/// <summary>
/// The Taint corruption meter, 0 to 100. At 50 or more the bearer takes -1 (status) to saves. Reaching 100
/// sets off the whispers: movement is reversed for 2 s and the meter falls back to 50.
/// </summary>
public sealed class TaintMeter(double rateMultiplier = 1.0)
{
    public const double Max = 100;
    public const double PenaltyThreshold = 50;
    public const double WhispersSeconds = 2.0;

    public double RateMultiplier { get; private set; } = rateMultiplier;

    /// <summary>Makes Taint build more slowly by a share (a feat): 0.25 means a quarter more slowly.</summary>
    public void SlowBy(double share) => RateMultiplier *= Math.Clamp(1 - share, 0, 1);

    internal void ResetRate(double rate) => RateMultiplier = rate;
    public double Value { get; private set; }
    public double ReversedMovementRemaining { get; private set; }

    /// <summary>The rate for an ancestry multiplier (Ashborn 0.75) and CON: 5% slower per point, never below half rate.</summary>
    public static double RateFor(double ancestryMultiplier, int constitution) =>
        ancestryMultiplier * Math.Max(0.5, 1.0 - 0.05 * constitution);

    /// <summary>Adds corruption. Returns true when it set off the whispers.</summary>
    public bool Add(double amount)
    {
        Value = Math.Clamp(Value + amount * RateMultiplier, 0, Max);
        if (Value < Max)
            return false;
        ReversedMovementRemaining = WhispersSeconds;
        Value = PenaltyThreshold;
        return true;
    }

    public void Cleanse(double amount = Max) => Value = Math.Max(0, Value - amount);

    internal void Restore(double value) => Value = Math.Clamp(value, 0, Max - 1e-6);

    public void Update(double deltaSeconds) =>
        ReversedMovementRemaining = Math.Max(0, ReversedMovementRemaining - deltaSeconds);

    public Modifier? SavePenalty => Value >= PenaltyThreshold ? new Modifier("Tainted", -1, BonusType.Status) : null;
}
