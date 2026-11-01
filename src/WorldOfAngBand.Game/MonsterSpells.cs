using System.Globalization;
using WorldOfAngBand.Engine.Combat;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Monsters;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>
/// Angband's 91 monster spells and innate attacks (monster_spell.txt, as AVABand converted it) in Pathfinder's terms (the
/// spec's section 6.1). Damage is Angband's (its formula at the monster's spell power, a breath's share of the monster's
/// hit points), scaled for depth like everything else a monster does. Then:
/// <list type="bullet">
/// <item>bolts, arrows and boulders are attacks against AC, counted in the monster's round;</item>
/// <item>balls, breaths and storms call for a basic Reflex save, mind blasts and wounds for a basic Will save;</item>
/// <item>blinding, confusion, fear, slowing and paralysis call for a save, and a worn protection turns them aside;</item>
/// <item>an element's rider (the spec's section 6.2) comes with a failed save or a hit.</item>
/// </list>
/// A monster casts on one turn in its spell frequency (innate attacks on their own frequency), with a clear line to the
/// hero; healing and escapes only when hurt. Laying traps, spinning webs and changing shape wait for those things to
/// come into the game (<see cref="Deferred"/>).
/// </summary>
internal sealed class MonsterSpells(DungeonSession session)
{
    /// <summary>The spell kinds not cast yet: there are no traps, webs or shapes in the game.</summary>
    public static readonly IReadOnlySet<MonsterSpellKind> Deferred = new HashSet<MonsterSpellKind>
    {
        MonsterSpellKind.Traps, MonsterSpellKind.Web, MonsterSpellKind.Shapechange,
    };

    private Hero Hero => session.Hero;
    private RulesRandom Roller => session.Roller;

    /// <summary>Whether the monster casts this turn, and if it does, the spell (Angband make_ranged_attack). True when it cast.</summary>
    public bool TryCast(Foe foe)
    {
        var monster = foe.Monster;
        var race = monster.Race;
        // One of AshenReliquary's archers or casters: a shot or a spell, when the hero is in its range and in sight.
        if (foe.Ashen is { Creature.Ranged: true } ashen && foe.Conditions.CanAct && monster.Confused == 0)
        {
            var distance = monster.Position.DistanceTo(Hero.Position);
            if (distance > 1.5 && distance <= ashen.Creature.AttackRange + 0.5 && session.Rng.OneIn(2)
                && ProjectionPath.Projectable(session.Level, monster.Position, Hero.Position, (int)Math.Ceiling(ashen.Creature.AttackRange) + 1))
            {
                session.Fight.AshenStrike(foe, ranged: true);
                return true;
            }
            return false;
        }
        if (race.Spells.Count == 0 || monster.Confused > 0 || !foe.Conditions.CanAct)
            return false;
        if (!ProjectionPath.Projectable(session.Level, monster.Position, Hero.Position, session.Data.Constants.MaxSight))
            return false;
        var rng = session.Rng;
        bool? innate = race.InnateFrequency > 0 && rng.OneIn(race.InnateFrequency) ? true
            : race.SpellFrequency > 0 && rng.OneIn(race.SpellFrequency) ? false
            : null;
        if (innate is null)
            return false;
        var choices = race.Spells.Select(session.Data.MonsterSpell).OfType<MonsterSpellDef>()
            .Where(s => s.Innate == innate && Worthwhile(foe, s)).ToList();
        if (choices.Count == 0)
            return false;
        Cast(foe, rng.Pick(choices));
        return true;
    }

    /// <summary>
    /// Angband's spell choice, simplified: no healing unhurt, no escape unless hurt or afraid, nothing pointless. The
    /// story's keepers (Saint Ysmere, the Cinder) hold their ground: they never teleport away, or send the hero off their
    /// level.
    /// </summary>
    private bool Worthwhile(Foe foe, MonsterSpellDef spell)
    {
        var m = foe.Monster;
        var near = m.Position.ChebyshevTo(Hero.Position) <= 1;
        return spell.Kind switch
        {
            _ when Deferred.Contains(spell.Kind) => false,
            MonsterSpellKind.Blink or MonsterSpellKind.Teleport or MonsterSpellKind.TeleportLevel when m.Race.Has(MonsterFlags.Questor) => false,
            MonsterSpellKind.Heal => m.Hp < m.MaxHp * 7 / 10,
            MonsterSpellKind.Blink or MonsterSpellKind.Teleport => m.Hp < m.MaxHp / 2 || m.IsAfraid,
            MonsterSpellKind.TeleportTo or MonsterSpellKind.TeleportSelfTo => !near,
            MonsterSpellKind.Haste => m.Fast == 0,
            MonsterSpellKind.HealKin => session.Foes.Any(f => f != foe && f.Monster.Race.Base == m.Race.Base && f.Monster.Hp < f.Monster.MaxHp && session.Seen(f)),
            MonsterSpellKind.DrainMana => m.Hp < m.MaxHp,
            _ => true,
        };
    }

    private void Cast(Foe foe, MonsterSpellDef spell)
    {
        var monster = foe.Monster;
        var seen = session.Seen(foe);
        var name = seen ? session.Subject(foe.Monster) : "It";
        session.Say(seen ? Message(spell.Message, name) : spell.UnseenMessage ?? "Something mumbles.");
        session.Publish(new SpellEvent(monster.Position, Hero.Position, spell.Kind, spell.Element, spell.Kind == MonsterSpellKind.Ball ? 2 : 0, spell.Id));
        var element = spell.Element ?? spell.Id switch
        {
            "ARROW" or "BOLT" or "SHOT" => "arrow", "MISSILE" => "missile", "BA_HOLY" => "holy_orb", _ => null,
        };
        switch (spell.Kind)
        {
            case MonsterSpellKind.Bolt:
                Bolt(foe, spell, element);
                break;
            case MonsterSpellKind.Ball:
            case MonsterSpellKind.Breath:
                Save(foe, StatisticKind.Reflex, Damage(foe, spell), element);
                break;
            case MonsterSpellKind.Storm:
                foreach (var e in new[] { "water", "elec", "ice" })
                    Save(foe, StatisticKind.Reflex, Damage(foe, spell) / 3, e);
                break;
            case MonsterSpellKind.Mind:
                if (Save(foe, StatisticKind.Will, Damage(foe, spell), null, DamageType.Mental) < DegreeOfSuccess.Success)
                    Status(foe, spell);
                break;
            case MonsterSpellKind.Wound:
                Save(foe, StatisticKind.Will, Damage(foe, spell), null, DamageType.Void);
                break;
            case MonsterSpellKind.Status:
                Status(foe, spell);
                break;
            case MonsterSpellKind.Forget:
                if (Check.Roll(Roller, Hero.Character.Save(StatisticKind.Will).Total, foe.SaveDc).Degree < DegreeOfSuccess.Success)
                    session.Forget();
                else
                    session.Say("You resist the effects!");
                break;
            case MonsterSpellKind.Heal:
                var healed = CreatureBridge.ShareOfHitPoints(spell.HealPerLevel * Math.Max(1, monster.Race.Power), monster.Race, monster.MaxHp);
                monster.Hp = Math.Min(monster.MaxHp, monster.Hp + healed);
                if (seen)
                    session.Say($"{name} looks healthier.");
                break;
            case MonsterSpellKind.HealKin:
                foreach (var kin in session.Foes.Where(f => f != foe && f.Monster.Race.Base == monster.Race.Base && session.Seen(f)))
                    kin.Monster.Hp = Math.Min(kin.Monster.MaxHp, kin.Monster.Hp + CreatureBridge.ShareOfHitPoints(spell.HealPerLevel * Math.Max(1, monster.Race.Power), kin.Monster.Race, kin.Monster.MaxHp));
                break;
            case MonsterSpellKind.DrainMana:
                // There is no mana to drain from a fighter: it drinks of the Taint instead, a little.
                monster.Hp = Math.Min(monster.MaxHp, monster.Hp + Math.Max(1, monster.Race.Power / 2));
                break;
            case MonsterSpellKind.Blink:
                session.TeleportMonster(monster, 10);
                break;
            case MonsterSpellKind.Teleport:
                session.TeleportMonster(monster, 60);
                break;
            case MonsterSpellKind.TeleportSelfTo:
                session.MoveMonsterNear(monster, Hero.Position);
                break;
            case MonsterSpellKind.TeleportTo:
                if (Resisted(foe, StatisticKind.Will))
                    break;
                session.PullHeroTo(monster.Position);
                break;
            case MonsterSpellKind.TeleportAway:
                if (!Resisted(foe, StatisticKind.Will))
                    session.Teleport(60);
                break;
            case MonsterSpellKind.TeleportLevel:
                if (!Resisted(foe, StatisticKind.Will))
                    session.TeleportLevel();
                break;
            case MonsterSpellKind.Summon:
                Summon(foe, spell);
                break;
            case MonsterSpellKind.Shriek:
                session.Aggravate();
                break;
            case MonsterSpellKind.Haste:
                monster.Fast = 20;
                if (seen)
                    session.Say($"{name} starts moving faster.");
                break;
            case MonsterSpellKind.Darkness:
                session.DarkenArea(Hero.Position, 3);
                if (!Protected("blind"))
                    Hero.Character.Conditions.Apply(ConditionKind.Blinded, 1, 2);
                break;
        }
    }

    /// <summary>A bolt: an attack against AC (a spell attack, or an arrow's ranged Strike), counted in the monster's round.</summary>
    private void Bolt(Foe foe, MonsterSpellDef spell, string? element)
    {
        var penalty = foe.Round.AttackPenalty(agile: false);
        foe.Round.Strike();
        var check = Check.Roll(Roller, foe.AttackModifier().Total + penalty, Hero.Character.ArmorClass().Total);
        if (check.Degree < DegreeOfSuccess.Success)
        {
            session.Say($"It misses you ({check.Natural} {(check.Modifier < 0 ? "−" : "+")} {Math.Abs(check.Modifier)} = {check.Total} vs AC {check.Dc}).");
            return;
        }
        var (type, rider) = Elements.For(element);
        if (spell.Id is "BOULDER" or "WHIP")
            type = spell.Id == "BOULDER" ? DamageType.Bludgeoning : DamageType.Slashing;
        var damage = DamageCalculator.Compute(Damage(foe, spell), 0, check.Degree, type, Hero.Character.Defenses);
        Hurt(foe, damage, type, $"({check.Natural} {(check.Modifier < 0 ? "−" : "+")} {Math.Abs(check.Modifier)} = {check.Total} vs AC {check.Dc})");
        Ride(rider, DegreeOfSuccess.Failure);
    }

    /// <summary>A basic save against damage: none, half, all or double by the degree. Returns the degree.</summary>
    private DegreeOfSuccess Save(Foe foe, StatisticKind save, int rolled, string? element, DamageType? as_ = null)
    {
        var (type, rider) = Elements.For(element);
        type = as_ ?? type;
        var check = Check.Roll(Roller, Hero.Character.Save(save).Total, foe.SaveDc);
        var damage = DamageCalculator.BasicSave(rolled, check.Degree, type, Hero.Character.Defenses);
        var word = save.ToString();
        Hurt(foe, damage, type, $"({word} {check.Natural} {(check.Modifier < 0 ? "−" : "+")} {Math.Abs(check.Modifier)} = {check.Total} vs DC {check.Dc}: {Degree(check.Degree)})");
        if (check.Degree < DegreeOfSuccess.Success)
            Ride(rider, check.Degree);
        return check.Degree;
    }

    private static string Degree(DegreeOfSuccess degree) => degree switch
    {
        DegreeOfSuccess.CriticalSuccess => "critical success",
        DegreeOfSuccess.Success => "success",
        DegreeOfSuccess.Failure => "failure",
        _ => "critical failure",
    };

    private void Hurt(Foe foe, int damage, DamageType type, string roll)
    {
        if (damage <= 0)
        {
            session.Say($"You are unharmed {roll}.");
            return;
        }
        session.Say($"You take {damage} {type.ToString().ToLowerInvariant()} {roll}.");
        session.Publish(new StrikeEvent(foe.Monster.Position, Hero.Position, false, DegreeOfSuccess.Success, damage));
        session.HurtHero(damage, session.Called(foe));
    }

    /// <summary>A save that negates (teleports): true when the hero resisted.</summary>
    private bool Resisted(Foe foe, StatisticKind save)
    {
        if (Check.Roll(Roller, Hero.Character.Save(save).Total, foe.SaveDc).Degree < DegreeOfSuccess.Success)
            return false;
        session.Say("You resist the effects!");
        return true;
    }

    private bool Protected(string protection)
    {
        if (!Hero.Gear.Protects(protection))
            return false;
        session.Belongings.Learn(RuneIds.Resist(protection));
        session.Say("You are unaffected!");
        return true;
    }

    /// <summary>A status spell: a save (Fortitude against blinding, slowing and paralysis, Will against the mind), a worn protection first.</summary>
    private void Status(Foe foe, MonsterSpellDef spell)
    {
        if (spell.PreventedBy is { } protection && Protected(protection))
            return;
        var save = spell.Timed is "confused" or "afraid" or "amnesia" ? StatisticKind.Will : StatisticKind.Fortitude;
        var check = Check.Roll(Roller, Hero.Character.Save(save).Total, foe.SaveDc);
        if (check.Degree >= DegreeOfSuccess.Success)
        {
            session.Say("You resist the effects!");
            return;
        }
        // Angband's turns are the game's half seconds; a critical failure doubles them.
        var seconds = Math.Max(1, spell.Duration.Roll(session.Rng)) * 0.5 * (check.Degree == DegreeOfSuccess.CriticalFailure ? 2 : 1);
        var c = Hero.Character.Conditions;
        switch (spell.Timed)
        {
            case "blind":
                c.Apply(ConditionKind.Blinded, 1, seconds);
                session.Say("You are blind!");
                break;
            case "confused":
                c.Apply(ConditionKind.Stupefied, check.Degree == DegreeOfSuccess.CriticalFailure ? 2 : 1, seconds);
                session.Say("You are confused!");
                break;
            case "afraid":
                c.Apply(ConditionKind.Frightened, check.Degree == DegreeOfSuccess.CriticalFailure ? 2 : 1);
                session.Say("You are terrified!");
                break;
            case "slow":
                c.Apply(ConditionKind.Slowed, 1, seconds);
                session.Say("You feel yourself moving slower!");
                break;
            case "paralyzed":
                c.Apply(ConditionKind.Stunned, 1, seconds);
                session.Say("You are paralysed!");
                break;
        }
    }

    /// <summary>An element's rider on the hero.</summary>
    private void Ride(Rider rider, DegreeOfSuccess degree)
    {
        var c = Hero.Character;
        var worse = degree == DegreeOfSuccess.CriticalFailure ? 2 : 1;
        switch (rider)
        {
            case Rider.Drain when !Protected("hold_life"):
                c.Conditions.Apply(ConditionKind.Drained, worse);
                session.Say("You feel your life draining away!");
                break;
            case Rider.Stun when !Hero.Gear.Protects("stun"):
                c.Conditions.Apply(ConditionKind.Stunned, 1, 0.5 * worse);
                session.Say("You are stunned!");
                break;
            case Rider.Bleed:
                c.Persistent.Add(DamageType.Bleed, new Dice(worse, 4));
                session.Say("You are bleeding!");
                break;
            case Rider.Displace:
                session.Teleport(8);
                break;
            case Rider.Confuse when !Protected("conf"):
                c.Conditions.Apply(ConditionKind.Stupefied, worse, 3);
                session.Say("You are confused!");
                break;
            case Rider.Slow when !Protected("free_act"):
                c.Conditions.Apply(ConditionKind.Slowed, 1, 2 * worse);
                session.Say("You feel yourself moving slower!");
                break;
            case Rider.Blind when !Protected("blind"):
                c.Conditions.Apply(ConditionKind.Blinded, 1, 2 * worse);
                session.Say("You are blind!");
                break;
            case Rider.Disenchant:
                if (Hero.Pack.Equipped.Where(i => i.ToHit > 0 || i.ToDam > 0 || i.ToAc > 0).OrderBy(_ => session.Rng.RandInt0(100)).FirstOrDefault() is { } item)
                {
                    if (item.ToHit > 0) item.ToHit--;
                    if (item.ToDam > 0) item.ToDam--;
                    if (item.ToAc > 0) item.ToAc--;
                    session.Belongings.Refit();
                    session.Say($"Your {session.NameOf(item)} was disenchanted!");
                }
                break;
        }
    }

    /// <summary>Calls up monsters of the summon's kind round the hero (Angband summon_specific).</summary>
    private void Summon(Foe foe, MonsterSpellDef spell)
    {
        var kind = spell.Summon is { } id ? session.Data.Summon(id) : null;
        var caster = foe.Monster.Race;
        bool Fits(MonsterRaceDef r)
        {
            if (r.IsUnique && kind?.Uniques != true)
                return false;
            if (kind is null)
                return true;
            if (kind.Id == "KIN")
                return r.Base == caster.Base;
            if (kind.Bases.Count > 0 && !kind.Bases.Contains(r.Base))
                return false;
            return kind.RaceFlag is not { } flag || r.Has(flag);
        }
        var count = Math.Max(1, spell.Count.Roll(session.Rng));
        var placed = session.SummonNear(Hero.Position, Math.Min(count, 6), Math.Max(1, (caster.Depth + session.Depth) / 2), Fits);
        if (placed == 0)
            session.Say("Nothing answers.");
    }

    /// <summary>A spell's damage: Angband's formula at the monster's spell power (or a breath's share of its hit points), scaled for depth, at most Pathfinder's for the monster's level.</summary>
    private int Damage(Foe foe, MonsterSpellDef spell) => Math.Min(AngbandDamage(foe, spell), CreatureBridge.SpellCap(foe.Level, foe.Stats.Rank));

    private int AngbandDamage(Foe foe, MonsterSpellDef spell)
    {
        var race = foe.Monster.Race;
        if (spell.Kind == MonsterSpellKind.Breath)
        {
            // A breath is a share of the monster's hit points: Angband's share of what it has left of its Angband hit
            // points, scaled for depth as damage is (and capped as Angband caps it).
            var monster = foe.Monster;
            var angbandHp = (double)race.HitPoints * Math.Max(1, monster.Hp) / Math.Max(1, monster.MaxHp);
            var breath = (int)(angbandHp / Math.Max(1, spell.BreathDivisor));
            return CreatureBridge.Scaled(Math.Min(breath, spell.BreathCap), race.Depth);
        }
        var rng = session.Rng;
        int raw;
        if (spell.DamageFormula is { } formula)
            raw = Formula(formula, spell.FormulaTerms, race.Power, rng);
        else if (spell.Damage.Count > 0)
            raw = new Engine.Randomness.Dice(spell.Damage.Count + (spell.LevelDivisor > 0 ? race.Power / spell.LevelDivisor : 0), spell.Damage.Sides, spell.Damage.Bonus).Roll(rng);
        else if (spell.Kind == MonsterSpellKind.Wound)
            raw = new Engine.Randomness.Dice(1 + race.Power / 3, 8, 0).Roll(rng);
        else
            raw = 0;
        return BridgeScaled(foe, raw);
    }

    private int BridgeScaled(Foe foe, int amount) => CreatureBridge.Scaled(amount, foe.Monster.Race.Depth);

    /// <summary>
    /// Angband's damage formula: "$B+6d8" with B = "SPELL_POWER:/ 3": each $-term is the spell power run through its
    /// operations in order, then the sum of the dice and numbers.
    /// </summary>
    public static int Formula(string formula, IReadOnlyDictionary<string, string> terms, int power, Engine.Randomness.GameRandom rng)
    {
        foreach (var (key, expression) in terms)
            formula = formula.Replace("$" + key, Term(expression, power).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var total = 0;
        foreach (var part in formula.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var d = part.IndexOf('d');
            if (d < 0)
                total += int.Parse(part, CultureInfo.InvariantCulture);
            else
            {
                var count = d == 0 ? 1 : int.Parse(part[..d], CultureInfo.InvariantCulture);
                var sides = int.Parse(part[(d + 1)..], CultureInfo.InvariantCulture);
                if (count > 0 && sides > 0)
                    total += new Engine.Randomness.Dice(count, sides, 0).Roll(rng);
            }
        }
        return total;
    }

    /// <summary>"SPELL_POWER:* 3 / 2 + 30": the power, then each operation in turn.</summary>
    private static int Term(string expression, int power)
    {
        var colon = expression.IndexOf(':');
        var tokens = (colon < 0 ? "" : expression[(colon + 1)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var value = power;
        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            var n = int.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
            value = tokens[i] switch
            {
                "+" => value + n,
                "-" => value - n,
                "*" => value * n,
                "/" => n == 0 ? value : value / n,
                _ => value,
            };
        }
        return Math.Max(0, value);
    }

    private static string Message(string template, string name) => template.Replace("{name}", name, StringComparison.Ordinal)
        .Replace("{pronoun}", "its", StringComparison.Ordinal);
}
