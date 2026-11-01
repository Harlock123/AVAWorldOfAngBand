using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Items;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>
/// Blows traded in melee, on Pathfinder's rules: the hero's Strikes against a monster's AC, and a monster's Angband
/// blows as Strikes against the hero's, each a d20 with degrees of success. A blow's effect (poison, fear, paralysis…)
/// comes with a save against the monster's DC (the spec's section 6.1). Every roll is written in the message log.
/// </summary>
internal sealed class Fight(DungeonSession session)
{
    private RulesRandom Roller => session.Roller;

    /// <summary>The hero Strikes a monster with their weapon: one action.</summary>
    public void HeroStrikes(Foe foe)
    {
        var hero = session.Hero;
        var character = hero.Character;
        var weapon = character.Weapon;
        var agile = weapon.Definition.Has(WeaponTrait.Agile);
        var penalty = hero.Round.AttackPenalty(agile);
        hero.Round.Strike();
        var name = session.Seen(foe) ? $"the {foe.Name}" : "it";
        // Asleep or held fast, it can't defend itself: off-guard.
        var offGuard = foe.Monster.IsAsleep || foe.Monster.Held > 0 ? -ConditionSet.OffGuardAcPenalty : 0;
        var ac = foe.ArmorClass().Total + offGuard;
        var attack = character.AttackModifier().Total + penalty;
        var check = Check.Roll(Roller, attack, ac);
        var roll = Roll(check, penalty);
        if (check.Degree < DegreeOfSuccess.Success)
        {
            session.Say($"You miss {name} {roll}.");
            session.Publish(new StrikeEvent(hero.Position, foe.Monster.Position, true, check.Degree, 0));
            Wake(foe);
            return;
        }
        var critical = check.Degree == DegreeOfSuccess.CriticalSuccess;
        var definition = weapon.Definition;
        var die = critical && definition.FatalDie is { } fatal ? fatal : definition.DamageDie;
        var dice = new Dice(weapon.DiceCount, die).Roll(Roller);
        var extra = critical ? Strike.CriticalExtraDice(weapon).Roll(Roller) : 0;
        var type = definition.TypeAgainst(foe.Defenses);
        var damage = DamageCalculator.Compute(dice, character.DamageBonus().Total, check.Degree, type, foe.Defenses, GrazeEffect.None, extra);
        // A hit shows the weapon's potency and striking for what they are.
        if (hero.Gear.Weapon is not null)
        {
            session.Belongings.Learn(Engine.Definitions.RuneIds.ToHit);
            session.Belongings.Learn(Engine.Definitions.RuneIds.ToDam);
        }
        var runes = new List<string>();
        foreach (var rune in hero.Gear.Extras)
        {
            if (rune.MonsterFlag is { } flag && !foe.Monster.Race.Has(flag))
                continue;
            // A rune's dice are doubled on a critical hit, as the weapon's are.
            var rolled = new Dice(rune.Dice * (critical ? 2 : 1), rune.Sides).Roll(Roller);
            var dealt = DamageCalculator.ApplyDefenses(rolled, rune.Type, foe.Defenses);
            if (dealt <= 0)
                continue;
            damage += dealt;
            runes.Add($"{dealt} {(rune.MonsterFlag is null ? rune.Type.ToString().ToLowerInvariant() : "bane")}");
            session.Belongings.Learn(rune.Rune);
        }
        var more = runes.Count > 0 ? $" ({string.Join(", ", runes)})" : "";
        session.Say($"You {(critical ? "critically hit" : "hit")} {name} {roll}: {damage} {type.ToString().ToLowerInvariant()}{more}.");
        session.Publish(new StrikeEvent(hero.Position, foe.Monster.Position, true, check.Degree, damage));
        Hurt(foe, damage);
    }

    /// <summary>
    /// A monster's melee action: each of its Angband blows as an agile Strike, the multiple attack penalty counting
    /// every Strike in its round (−4, then −8).
    /// </summary>
    public void MonsterMelee(Foe foe)
    {
        var hero = session.Hero;
        var seen = session.Seen(foe);
        var name = seen ? $"The {foe.Name}" : "It";
        var flanked = session.Flanked() ? -ConditionSet.OffGuardAcPenalty : 0;
        var ac = hero.Character.ArmorClass().Total + flanked;
        var blows = foe.Monster.Race.Blows;
        for (var i = 0; i < blows.Count && !hero.Character.IsDead && foe.Monster.IsActive; i++)
        {
            var blow = blows[i];
            if (blow.Method is "beg" or "insult" or "moan" or "drool")
            {
                if (seen)
                    session.Say($"{name} {Verb(blow.Method)} you.");
                continue;
            }
            // A monster's blows are agile Strikes, counted across its round as the hero's are.
            var penalty = foe.Round.AttackPenalty(agile: true);
            foe.Round.Strike();
            var check = Check.Roll(Roller, foe.AttackModifier().Total + penalty, ac);
            var roll = Roll(check, penalty);
            if (check.Degree < DegreeOfSuccess.Success)
            {
                session.Say($"{name} misses you {roll}.");
                session.Publish(new StrikeEvent(foe.Monster.Position, hero.Position, false, check.Degree, 0));
                continue;
            }
            var type = TypeOf(blow);
            var rolled = blow.Damage.Roll(session.Rng);
            var damage = rolled <= 0 ? 0 : DamageCalculator.Compute(rolled, 0, check.Degree, type, hero.Character.Defenses);
            // Being hit shows the armour's potency; a resistance that took something off shows itself.
            session.Belongings.Learn(Engine.Definitions.RuneIds.ToAc);
            if (rolled > 0 && hero.Character.Defenses.Resistances.ContainsKey(type) && ResistRune(type) is { } resisted)
                session.Belongings.Learn(resisted);
            var blocked = ShieldBlock(damage);
            damage -= blocked;
            var crit = check.Degree == DegreeOfSuccess.CriticalSuccess ? ", critically" : "";
            var shield = blocked > 0 ? $", {blocked} blocked by your shield" : "";
            session.Say(rolled > 0
                ? $"{name} {Verb(blow.Method)} you{crit} {roll}: {damage} {type.ToString().ToLowerInvariant()}{shield}."
                : $"{name} {Verb(blow.Method)} you {roll}.");
            session.Publish(new StrikeEvent(foe.Monster.Position, hero.Position, false, check.Degree, damage));
            if (damage > 0)
                session.HurtHero(damage, session.Called(foe));
            if (!hero.Character.IsDead)
                Effect(foe, blow, check.Degree == DegreeOfSuccess.CriticalSuccess);
        }
    }

    /// <summary>The rune of a worn item's resistance to an element, if one is worn.</summary>
    private string? ResistRune(DamageType type)
    {
        var element = type switch
        {
            DamageType.Fire => "fire", DamageType.Cold => "cold", DamageType.Acid => "acid", DamageType.Electricity => "elec",
            DamageType.Poison => "pois", _ => null,
        };
        return element is not null && session.Hero.Pack.Equipped.Any(i => i.Resists.Contains(element))
            ? Engine.Definitions.RuneIds.Resist(element)
            : null;
    }

    /// <summary>
    /// Shield Block, the hero's reaction (one a round): a shield takes its hardness off a blow that hurts, more for a
    /// Bulwark. Returns how much it took off.
    /// </summary>
    private int ShieldBlock(int damage)
    {
        var hero = session.Hero;
        if (damage <= 0 || !hero.Round.ReactionReady
            || hero.Pack.InSlot(Engine.Definitions.EquipSlot.Shield) is not { } shield || session.ItemBridge.ShieldFor(shield) is not { } bridged)
            return 0;
        hero.Round.UseReaction();
        return Math.Min(damage, bridged.Hardness + hero.Character.BlockHardnessBonus);
    }

    /// <summary>Damage to a monster: it wakes, may lose its nerve (Angband mon_take_hit), and dies below 0 HP.</summary>
    public void Hurt(Foe foe, int damage)
    {
        var monster = foe.Monster;
        Wake(foe);
        monster.Hp -= damage;
        if (monster.Hp < 0)
        {
            session.Kill(foe);
            return;
        }
        if (monster.Race.Has(MonsterFlags.NoFear) || damage <= 0)
            return;
        var percentage = 100L * monster.Hp / Math.Max(1, monster.MaxHp);
        var rng = session.Rng;
        if (rng.RandInt1(10) >= percentage || (damage >= monster.Hp && rng.RandInt0(100) < 80))
        {
            monster.Fear = Math.Max(monster.Fear, rng.RandInt1(10) + (damage >= monster.Hp && percentage > 7 ? 20 : (11 - (int)percentage) * 5));
            if (session.Seen(foe))
                session.Say($"The {foe.Name} flees in terror!");
        }
    }

    private static void Wake(Foe foe)
    {
        foe.Monster.Sleep = 0;
        foe.Monster.Aware = true;
    }

    /// <summary>"(14 + 7 = 21 vs AC 18)", the multiple attack penalty included in the modifier.</summary>
    private static string Roll(CheckResult check, int penalty) =>
        $"({check.Natural} {(check.Modifier < 0 ? "−" : "+")} {Math.Abs(check.Modifier)} = {check.Total} vs AC {check.Dc}{(penalty != 0 ? $", −{-penalty} for the attacks before it" : "")})";

    /// <summary>A blow's damage type: its element, or by how it lands.</summary>
    private static DamageType TypeOf(MonsterBlowDef blow) => blow.Effect switch
    {
        "fire" => DamageType.Fire,
        "cold" => DamageType.Cold,
        "acid" => DamageType.Acid,
        "elec" => DamageType.Electricity,
        _ => blow.Method switch
        {
            "bite" or "sting" or "butt" => DamageType.Piercing,
            "claw" => DamageType.Slashing,
            "spore" => DamageType.Poison,
            _ => DamageType.Bludgeoning,
        },
    };

    private static string Verb(string method) => method switch
    {
        "hit" => "hits",
        "bite" => "bites",
        "claw" => "claws",
        "sting" => "stings",
        "touch" => "touches",
        "crush" => "crushes",
        "crawl" => "crawls on",
        "spore" => "releases spores at",
        "gaze" => "gazes at",
        "butt" => "butts",
        "kick" => "kicks",
        "spit" => "spits on",
        "engulf" => "engulfs",
        "beg" => "begs",
        "insult" => "insults",
        "moan" => "moans at",
        "drool" => "drools on",
        _ => method + "s",
    };

    /// <summary>
    /// A blow's effect, if the hero fails a save against the monster's DC (worse on a critical failure, or when the
    /// blow was a critical hit, which counts as one degree worse).
    /// </summary>
    private void Effect(Foe foe, MonsterBlowDef blow, bool criticalHit)
    {
        var (save, apply) = blow.Effect switch
        {
            "poison" => (StatisticKind.Fortitude, (Action<int>)(n =>
            {
                session.Hero.Character.Persistent.Add(DamageType.Poison, new Dice(n, 4));
                session.Say("You are poisoned!");
            })),
            "terrify" => (StatisticKind.Will, n => Condition(ConditionKind.Frightened, n, null, "You are frightened!")),
            "confuse" => (StatisticKind.Will, n => Condition(ConditionKind.Stupefied, n, 6, "You are confused!")),
            "paralyze" => (StatisticKind.Fortitude, n => Condition(ConditionKind.Stunned, 1, n, "You are paralysed!")),
            "blind" => (StatisticKind.Fortitude, n => Condition(ConditionKind.Blinded, 1, 3 * n, "You can't see!")),
            "lose_str" => (StatisticKind.Fortitude, n => Condition(ConditionKind.Enfeebled, n, null, "You feel very weak.")),
            "lose_dex" => (StatisticKind.Fortitude, n => Condition(ConditionKind.Clumsy, n, null, "You feel very clumsy.")),
            "lose_con" or "exp_10" or "exp_20" or "exp_40" or "exp_80" =>
                (StatisticKind.Fortitude, n => Condition(ConditionKind.Drained, n, null, "You feel your life draining away!")),
            "lose_int" or "lose_wis" => (StatisticKind.Will, n => Condition(ConditionKind.Stupefied, n, null, "Your mind clouds.")),
            "eat_gold" or "eat_item" or "eat_food" or "eat_light" => (StatisticKind.Reflex, _ => session.Say("A hand fumbles at your belt.")),
            _ => default((StatisticKind, Action<int>)),
        };
        if (apply is null)
            return;
        // A worn protection (free action, courage, sight, clarity, sustains, hold life) turns the effect aside.
        var protection = blow.Effect switch
        {
            "paralyze" => "free_act", "terrify" => "fear", "blind" => "blind", "confuse" => "conf",
            "lose_str" => "sust_str", "lose_dex" => "sust_dex", "lose_con" => "sust_con", "lose_int" => "sust_int", "lose_wis" => "sust_wis",
            "exp_10" or "exp_20" or "exp_40" or "exp_80" => "hold_life",
            _ => null,
        };
        if (protection is not null && session.Hero.Gear.Protects(protection))
        {
            session.Belongings.Learn(protection == "hold_life"
                ? Engine.Definitions.RuneIds.Flag(Engine.Definitions.ItemFlags.HoldLife)
                : Engine.Definitions.RuneIds.Resist(protection));
            session.Say("You are unaffected!");
            return;
        }
        var check = Check.Roll(Roller, session.Hero.Character.Save(save).Total, foe.SaveDc);
        var degree = criticalHit ? Check.StepDown(check.Degree) : check.Degree;
        if (degree >= DegreeOfSuccess.Success)
            return;
        apply(degree == DegreeOfSuccess.CriticalFailure ? 2 : 1);
    }

    private void Condition(ConditionKind kind, int value, double? seconds, string message)
    {
        session.Hero.Character.Conditions.Apply(kind, value, seconds);
        session.Say(message);
    }
}
