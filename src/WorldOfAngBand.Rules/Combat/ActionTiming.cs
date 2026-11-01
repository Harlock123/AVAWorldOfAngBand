namespace WorldOfAngBand.Rules.Combat;

/// <summary>
/// The action layer: Pathfinder's three-action turn turned into time. One action is 0.5 s of commitment,
/// and gear bonuses to speed are capped.
/// </summary>
public static class ActionTiming
{
    public const double SecondsPerAction = 0.5;
    public const double AttackSpeedCap = 0.60;
    public const double CastSpeedCap = 0.50;
    public const double MoveSpeedCap = 0.40;
    public const double CooldownReductionCap = 0.30;

    /// <summary>25 ft is the reference run speed of 5 tiles a second; other speeds scale linearly.</summary>
    public const double FeetPerTilePerSecond = 5.0;

    public const double CritFailStaggerSeconds = 0.5;
    public const double StepDistanceTiles = 2.0;
    public const double StepInvulnerableSeconds = 0.25;
    public const double StepCooldownSeconds = 4.0;
    public const double ReactionCooldownSeconds = 6.0;

    /// <summary>The wind-up or cast time of an ability that costs <paramref name="actions"/> actions.</summary>
    public static double CastTime(int actions, double castSpeedBonus = 0, double slowedMultiplier = 1.0) =>
        actions * SecondsPerAction * slowedMultiplier / (1.0 + Math.Clamp(castSpeedBonus, 0, CastSpeedCap));

    /// <summary>Seconds between weapon attacks.</summary>
    public static double AttackInterval(double attacksPerSecond, double attackSpeedBonus = 0, double slowedMultiplier = 1.0) =>
        slowedMultiplier / (attacksPerSecond * (1.0 + Math.Clamp(attackSpeedBonus, 0, AttackSpeedCap)));

    public static double MoveSpeedTilesPerSecond(int speedFeet, double moveSpeedBonus = 0) =>
        Math.Max(0, speedFeet) / FeetPerTilePerSecond * (1.0 + Math.Clamp(moveSpeedBonus, 0, MoveSpeedCap));

    public static double Cooldown(double baseSeconds, double cooldownReduction = 0) =>
        baseSeconds * (1.0 - Math.Clamp(cooldownReduction, 0, CooldownReductionCap));
}
