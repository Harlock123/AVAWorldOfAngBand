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

    /// <summary>The hero Strikes a monster with their weapon (one action; an ability's options change it). Returns the degree.</summary>
    public DegreeOfSuccess HeroStrikes(Foe foe, StrikeOptions? options = null)
    {
        var o = options ?? StrikeOptions.Plain;
        var hero = session.Hero;
        var character = hero.Character;
        var weapon = character.Weapon;
        var agile = weapon.Definition.Has(WeaponTrait.Agile);
        var penalty = hero.Round.AttackPenalty(agile);
        hero.Round.Strike();
        var name = session.Seen(foe) ? session.Object(foe.Monster) : "it";
        // Asleep or held fast, it can't defend itself: off-guard; so too against a feint, or out of hiding.
        var hidden = session.Powers.ComeOutOfHiding();
        var offGuard = foe.Monster.IsAsleep || foe.Monster.Held > 0 || o.OffGuard || hidden ? -ConditionSet.OffGuardAcPenalty : 0;
        var ac = foe.ArmorClass().Total + offGuard;
        var attack = character.AttackModifier().Total + penalty;
        var check = Check.Roll(Roller, attack, ac);
        var roll = Roll(check, penalty);
        if (check.Degree < DegreeOfSuccess.Success)
        {
            session.Say($"You miss {name} {roll}.");
            session.Publish(new StrikeEvent(hero.Position, foe.Monster.Position, true, check.Degree, 0));
            Wake(foe);
            return check.Degree;
        }
        var degree = o.SureCritical ? DegreeOfSuccess.CriticalSuccess : check.Degree;
        var critical = degree == DegreeOfSuccess.CriticalSuccess;
        hero.Resource.OnCombat(critical);
        var definition = weapon.Definition;
        var die = critical && definition.FatalDie is { } fatal ? fatal : definition.DamageDie;
        var dice = new Dice(weapon.DiceCount + o.ExtraDice, die).Roll(Roller);
        var extra = critical ? Strike.CriticalExtraDice(weapon).Roll(Roller) : 0;
        var type = definition.TypeAgainst(foe.Defenses);
        var damage = DamageCalculator.Compute(dice, character.DamageBonus().Total, degree, type, foe.Defenses, GrazeEffect.None, extra);
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
        session.Publish(new StrikeEvent(hero.Position, foe.Monster.Position, true, degree, damage));
        if (o.Drain > 0 && damage > 0)
            session.HealHero((int)Math.Round(damage * o.Drain));
        Hurt(foe, damage);
        if (foe.Monster.IsActive && !foe.Monster.IsRemoved)
            session.Powers.AfterHit(foe, o.OnHit, o.Persistent, o.Knockback, critical);
        return degree;
    }

    /// <summary>
    /// A monster's melee action: each of its Angband blows as an agile Strike, the multiple attack penalty counting
    /// every Strike in its round (−4, then −8).
    /// </summary>
    public void MonsterMelee(Foe foe)
    {
        if (foe.Ashen is not null)
        {
            AshenStrike(foe, ranged: false);
            return;
        }
        var hero = session.Hero;
        var seen = session.Seen(foe);
        var name = seen ? session.Subject(foe.Monster) : "It";
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
            // Angband's blow, scaled for depth as everything a monster does is, and at most Pathfinder's for its level.
            var rolled = Math.Min(Bridge.CreatureBridge.Scaled(blow.Damage.Roll(session.Rng), foe.Monster.Race.Depth), Bridge.CreatureBridge.BlowCap(foe.Level, foe.Stats.Rank));
            var damage = rolled <= 0 ? 0 : DamageCalculator.Compute(rolled, 0, check.Degree, type, hero.Character.Defenses);
            // Being hit shows the armour's potency; a resistance that took something off shows itself.
            session.Belongings.Learn(Engine.Definitions.RuneIds.ToAc);
            if (rolled > 0 && hero.Character.Defenses.Resistances.ContainsKey(type) && ResistRune(type) is { } resisted)
                session.Belongings.Learn(resisted);
            var blocked = ShieldBlock(damage);
            damage -= blocked;
            // A ward or an aura takes its share.
            var soaked = session.Powers.Soak(damage);
            damage -= soaked;
            blocked += soaked;
            var crit = check.Degree == DegreeOfSuccess.CriticalSuccess ? ", critically" : "";
            var shield = blocked > 0 ? $", {blocked} blocked by your shield" : "";
            session.Say(rolled > 0
                ? $"{name} {Verb(blow.Method)} you{crit} {roll}: {damage} {type.ToString().ToLowerInvariant()}{shield}."
                : $"{name} {Verb(blow.Method)} you {roll}.");
            session.Publish(new StrikeEvent(foe.Monster.Position, hero.Position, false, check.Degree, damage));
            if (damage > 0)
                session.HurtHero(damage, session.Called(foe));
            hero.Resource.OnCombat(taken: true);
            if (!hero.Character.IsDead && foe.Monster.IsActive)
                session.Powers.Retaliate(foe);
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
        damage = session.Twisted(damage);
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
                session.Say($"{session.Subject(foe.Monster)} flees in terror!");
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

    /// <summary>
    /// One of AshenReliquary's creatures strikes, as it struck there: one Strike a turn (a shot or a spell from afar
    /// when it is <paramref name="ranged"/>), its own dice at its rank's share, its damage type, its poison and the
    /// condition its hits leave (with the save that turns it aside).
    /// </summary>
    public void AshenStrike(Foe foe, bool ranged)
    {
        var hero = session.Hero;
        var ashen = foe.Ashen!;
        var creature = ashen.Creature;
        var seen = session.Seen(foe);
        var name = seen ? session.Subject(foe.Monster) : "It";
        var penalty = foe.Round.AttackPenalty(agile: false);
        foe.Round.Strike();
        var flanked = !ranged && session.Flanked() ? -ConditionSet.OffGuardAcPenalty : 0;
        var check = Check.Roll(Roller, foe.AttackModifier().Total + penalty, hero.Character.ArmorClass().Total + flanked);
        var roll = Roll(check, penalty);
        var verb = ranged ? creature.Caster ? "casts at" : "shoots at" : "hits";
        if (ranged)
            session.Publish(new SpellEvent(foe.Monster.Position, hero.Position, Engine.Definitions.MonsterSpellKind.Bolt, ElementOf(creature.DamageType), 0));
        if (check.Degree < DegreeOfSuccess.Success)
        {
            session.Say($"{name} {(ranged ? verb : "misses")} you{(ranged ? " and misses" : "")} {roll}.");
            session.Publish(new StrikeEvent(foe.Monster.Position, hero.Position, false, check.Degree, 0));
            return;
        }
        var rolled = Math.Max(1, (int)Math.Round(ashen.Strike.Roll(Roller) * ashen.DamageMultiplier));
        var damage = DamageCalculator.Compute(rolled, 0, check.Degree, creature.DamageType, hero.Character.Defenses);
        var blocked = 0;
        if (!ranged)
        {
            blocked = ShieldBlock(damage);
            damage -= blocked;
            var soaked = session.Powers.Soak(damage);
            damage -= soaked;
            blocked += soaked;
        }
        var crit = check.Degree == DegreeOfSuccess.CriticalSuccess ? ", critically" : "";
        session.Say($"{name} {verb} you{crit} {roll}: {damage} {creature.DamageType.ToString().ToLowerInvariant()}{(blocked > 0 ? $", {blocked} blocked" : "")}.");
        session.Publish(new StrikeEvent(foe.Monster.Position, hero.Position, false, check.Degree, damage));
        if (damage > 0)
            session.HurtHero(damage, session.Called(foe));
        hero.Resource.OnCombat(taken: true);
        if (hero.Character.IsDead)
            return;
        if (creature.PoisonOnHit is { } poison && session.Rng.RandInt0(1000) < creature.PoisonChance * 1000)
        {
            hero.Character.Persistent.Add(DamageType.Poison, Dice.Parse(poison));
            session.Say("You are poisoned!");
        }
        if (creature.Inflicts is { } inflict)
        {
            var save = Enum.TryParse<StatisticKind>(inflict.Save, out var kind) ? kind : StatisticKind.Fortitude;
            var saved = Check.Roll(Roller, hero.Character.Save(save).Total, foe.SaveDc).Degree;
            if (saved < DegreeOfSuccess.Success)
                Condition(inflict.Condition, inflict.Value + (saved == DegreeOfSuccess.CriticalFailure ? 1 : 0),
                    inflict.Seconds > 0 ? inflict.Seconds : null, $"You are {inflict.Condition.ToString().ToLowerInvariant()}!");
        }
        if (!ranged && foe.Monster.IsActive)
            session.Powers.Retaliate(foe);
    }

    private static string? ElementOf(DamageType type) => type switch
    {
        DamageType.Fire => "fire", DamageType.Cold => "cold", DamageType.Electricity => "elec", DamageType.Acid => "acid",
        DamageType.Poison => "pois", DamageType.Void => "nether", DamageType.Mental => "chaos", _ => "arrow",
    };

    private void Condition(ConditionKind kind, int value, double? seconds, string message)
    {
        session.Hero.Character.Conditions.Apply(kind, value, seconds);
        session.Say(message);
    }
}

/// <summary>
/// What an ability adds to a Strike: more weapon dice, a feint (the target off-guard), a sure critical hit, a share of
/// the harm drunk as life, and what a hit leaves behind (a condition, persistent damage, a shove).
/// </summary>
internal sealed record StrikeOptions(
    int ExtraDice = 0,
    bool OffGuard = false,
    bool SureCritical = false,
    double Drain = 0,
    OnHit? OnHit = null,
    (Dice Dice, DamageType Type)? Persistent = null,
    int Knockback = 0)
{
    public static readonly StrikeOptions Plain = new();
}

/// <summary>A condition an ability leaves on a hit or a failed save: its kind, value and seconds (0 for until it fades).</summary>
internal sealed record OnHit(ConditionKind Kind, int Value, double Seconds);
