using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Creatures;
using WorldOfAngBand.Rules.Items;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Combat;

/// <summary>What one attack did. <see cref="Check"/> is default when the attacker failed the flat check for impaired vision.</summary>
public sealed record StrikeResult(
    CheckResult Check,
    int MultipleAttackPenalty,
    int Damage,
    DamageType DamageType,
    bool AttackerStaggered,
    bool LostToImpairedVision,
    GrazeEffect Graze = GrazeEffect.None)
{
    public DegreeOfSuccess Degree => LostToImpairedVision ? DegreeOfSuccess.Failure : Check.Degree;

    /// <summary>A failure that still dealt half damage.</summary>
    public bool Grazed => Degree == DegreeOfSuccess.Failure && Graze == GrazeEffect.Half && !LostToImpairedVision && Damage > 0;

    /// <summary>The floating-text word the HUD shows: CRIT, hit, GRAZE (a failure that still deals half) or MISS.</summary>
    public string Label => Degree switch
    {
        DegreeOfSuccess.CriticalSuccess => "CRIT",
        DegreeOfSuccess.Success => "hit",
        DegreeOfSuccess.Failure when Graze == GrazeEffect.Half && !LostToImpairedVision => "GRAZE",
        _ => "MISS",
    };
}

/// <summary>
/// The hidden Pathfinder roll behind every real-time hit: d20 + attack modifier (with the cadence penalty)
/// against AC, a degree of success, then damage dice, the multiplier and the target's defences.
/// </summary>
public static class Strike
{
    /// <summary>
    /// A weapon Strike that fails by this much or less still grazes for half damage (spec, section 10: GRAZE, a failure
    /// with a partial effect). Real time made a run of plain misses feel limp; about one d20 roll in seven grazes.
    /// </summary>
    public const int NearMissMargin = 3;

    /// <summary>The graze a check comes to: <see cref="GrazeEffect.Near"/> becomes Half on a near miss, else None.</summary>
    public static GrazeEffect Effective(GrazeEffect graze, CheckResult check) => graze switch
    {
        GrazeEffect.Near => check.Degree == DegreeOfSuccess.Failure && check.Total >= check.Dc - NearMissMargin ? GrazeEffect.Half : GrazeEffect.None,
        _ => graze,
    };

    /// <param name="extraDice">Extra weapon damage dice (Power Attack adds 1, 2 at level 10, 3 at 18).</param>
    /// <param name="cadenceAttacks">How many attacks this counts as for the cadence penalty (Power Attack: 2).</param>
    /// <param name="targetAc">Adjusts the target's AC for this Strike (−2 for one off-guard to it that has no condition saying so).</param>
    /// <param name="precisionDice">Sneak attack's d6s, added to the weapon's dice (so doubled on a critical hit).</param>
    /// <param name="hitIsCritical">A hit is a critical hit (the Cutthroat's Strike out of hiding).</param>
    /// <param name="preyDice">Hunt Prey's d8s against the hunted monster, precision like sneak attack.</param>
    public static StrikeResult Resolve(Character attacker, IDefender target, AttackCadence cadence, double now,
        IRandomSource random, GrazeEffect graze = GrazeEffect.None, double attackSpeedBonus = 0,
        int extraDice = 0, int cadenceAttacks = 1, int targetAc = 0, int precisionDice = 0, bool hitIsCritical = false,
        int preyDice = 0)
    {
        var weapon = attacker.Weapon;
        var swing = ActionTiming.AttackInterval(weapon.Definition.AttacksPerSecond, attackSpeedBonus,
            attacker.Conditions.ActionTimeMultiplier);
        var agile = weapon.Definition.Has(WeaponTrait.Agile);
        var penalty = cadence.Attack(now, swing, agile);
        for (var i = 1; i < cadenceAttacks; i++)
            cadence.Attack(now, swing, agile);
        if (attacker.Conditions.HasImpairedVision && !random.FlatCheck(ConditionSet.ConcealmentFlatCheckDc))
            return new StrikeResult(default, penalty, 0, weapon.Definition.DamageType, false, true);

        var check = Check.Roll(random, attacker.AttackModifier().Total + penalty, target.ArmorClass().Total + targetAc);
        if (hitIsCritical && check.Degree == DegreeOfSuccess.Success)
            check = check with { Degree = DegreeOfSuccess.CriticalSuccess };
        graze = Effective(graze, check);
        var damage = 0;
        if (DamageCalculator.DegreeMultiplier(check.Degree, graze) > 0)
        {
            var critical = check.Degree == DegreeOfSuccess.CriticalSuccess;
            var definition = weapon.Definition;
            var die = critical && definition.FatalDie is { } fatal ? fatal : definition.DamageDie;
            var diceTotal = new Dice(weapon.DiceCount + Math.Max(0, extraDice), die).Roll(random)
                + (precisionDice > 0 ? new Dice(precisionDice, 6).Roll(random) : 0)
                + (preyDice > 0 ? new Dice(preyDice, 8).Roll(random) : 0);
            var extra = critical ? CriticalExtraDice(weapon).Roll(random) : 0;
            damage = DamageCalculator.Compute(diceTotal, attacker.DamageBonus().Total, check.Degree,
                definition.TypeAgainst(target.Defenses), target.Defenses, graze, extra);
        }

        return new StrikeResult(check, penalty, damage, weapon.Definition.TypeAgainst(target.Defenses),
            check.Degree == DegreeOfSuccess.CriticalFailure, false, graze);
    }

    /// <summary>
    /// A creature's strike: its level-based attack bonus against the target's AC. Damage is scaled by the
    /// rank's solo-play damage multiplier before the target's defences. <paramref name="targetAc"/> adjusts the
    /// target's AC for this strike (−2 when flanked), <paramref name="attackBonus"/> and <paramref name="damageScale"/>
    /// carry a monster affix's boost (Vengeful).
    /// </summary>
    public static StrikeResult Resolve(Creature attacker, IDefender target, AttackCadence cadence, double now,
        IRandomSource random, int targetAc = 0, int attackBonus = 0, double damageScale = 1.0)
    {
        var swing = ActionTiming.AttackInterval(attacker.AttacksPerSecond, 0, attacker.Conditions.ActionTimeMultiplier);
        var penalty = cadence.Attack(now, swing, agile: false);
        var check = Check.Roll(random, attacker.AttackModifier().Total + penalty + attackBonus, target.ArmorClass().Total + targetAc);
        var damage = 0;
        if (DamageCalculator.DegreeMultiplier(check.Degree) > 0)
        {
            var rolled = attacker.StrikeDamage.Roll(random);
            var scaled = Math.Max(1, (int)Math.Round(rolled * attacker.Stats.DamageMultiplier * damageScale, MidpointRounding.AwayFromZero));
            damage = DamageCalculator.Compute(scaled, 0, check.Degree, attacker.StrikeDamageType, target.Defenses);
        }
        return new StrikeResult(check, penalty, damage, attacker.StrikeDamageType,
            check.Degree == DegreeOfSuccess.CriticalFailure, false);
    }

    /// <summary>
    /// A spell attack (Ignition): d20 + spell attack modifier against AC, then <paramref name="damage"/> through the
    /// degree multiplier and the target's defences. Spell damage adds no attribute, as in the Remaster.
    /// </summary>
    public static StrikeResult ResolveSpellAttack(Character caster, IDefender target, Dice damage, DamageType type,
        IRandomSource random, int targetAc = 0)
    {
        var check = Check.Roll(random, caster.SpellAttack().Total, target.ArmorClass().Total + targetAc);
        var dealt = DamageCalculator.DegreeMultiplier(check.Degree) > 0
            ? DamageCalculator.Compute(damage.RollDiceOnly(random), damage.Bonus, check.Degree, type, target.Defenses)
            : 0;
        return new StrikeResult(check, 0, dealt, type, false, false);
    }

    /// <summary>
    /// Dice added on a critical hit, after doubling. Fatal adds one die of the fatal size; deadly adds one die
    /// of its size (two with a greater striking rune, three with major).
    /// </summary>
    public static Dice CriticalExtraDice(Weapon weapon)
    {
        var definition = weapon.Definition;
        if (definition.FatalDie is { } fatal)
            return new Dice(1, fatal);
        if (definition.DeadlyDie is { } deadly)
            return new Dice(Math.Max(1, weapon.Striking), deadly);
        return new Dice(0, 1);
    }
}
