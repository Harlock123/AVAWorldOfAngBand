using WorldOfAngBand.Engine.Combat;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Rules.Abilities;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Engine.Time;
using WorldOfAngBand.Rules.Items;
using EquipSlot = WorldOfAngBand.Engine.Definitions.EquipSlot;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>
/// The hero's class abilities, AshenReliquary's (<c>Rules/Content/Data/abilities.json</c>), played one action at a time
/// (the spec's section 4.1). An ability takes its 1 to 3 actions, pays its cost from the class's resource (Fury, Mana,
/// Devotion, Momentum), and waits out its cooldown, which runs on the game's clock (half a second an action). A spell
/// heightens with the hero's level (rank = (level + 1) / 2). Wards, auras, hiding and traps last their seconds.
/// <list type="bullet">
/// <item>weapon Strikes take the multiple attack penalty as the hero's own do, with the ability's extra dice, feint,
/// condition and persistent damage;</item>
/// <item>spell bolts are spell attacks against AC; missiles always hit; bursts round the hero, cones and blasts are
/// basic saves against the spell DC (a spell) or the class DC;</item>
/// <item>an aura pulses every 2 s on the enemies round the hero.</item>
/// </list>
/// Abilities that need a companion, raised dead, a bow or a gun (Mark, Command, Summon, the belt) wait for those to come
/// into the game: classes that depend on them are not yet playable (<see cref="IsPlayable"/>).
/// </summary>
public sealed class Powers(DungeonSession session)
{
    /// <summary>One Flourish a round: three actions of half a second.</summary>
    public const double FlourishLockoutSeconds = 1.5;

    /// <summary>How often an aura pulses (AshenReliquary's AuraTickSeconds).</summary>
    public const double AuraPulseSeconds = 2.0;

    private static readonly HashSet<AbilityKind> Supported =
    [
        AbilityKind.WeaponStrike, AbilityKind.ChargeStrike, AbilityKind.SpellBolt, AbilityKind.Missiles, AbilityKind.Ward,
        AbilityKind.Burst, AbilityKind.Blast, AbilityKind.Heal, AbilityKind.Aura, AbilityKind.Tumble, AbilityKind.Hide,
        AbilityKind.Trap,
    ];

    private readonly Dictionary<string, double> _readyAt = [];
    private readonly List<LaidTrap> _traps = [];
    private double _flourishReadyAt;
    private double _blockReadyAt;
    private double _hiddenUntil;

    private Hero Hero => session.Hero;
    private RulesRandom Roller => session.Roller;

    /// <summary>Seconds of game time gone by (the cooldowns' clock).</summary>
    public double Clock { get; private set; }

    /// <summary>The ward up, if any (Raise a Shield, the Shield cantrip, Barkskin).</summary>
    public ActiveWard? Ward { get; private set; }

    /// <summary>The aura or stance about the hero, if any.</summary>
    public ActiveAura? Aura { get; private set; }

    public bool Hidden => Clock < _hiddenUntil;

    public IReadOnlyList<LaidTrap> Traps => _traps;

    /// <summary>Whether a class can be played yet: none of its own abilities needs something the game lacks.</summary>
    public static bool IsPlayable(Rules.Content.ContentDatabase content, string classId) =>
        content.Abilities.Values.Where(a => a.Class == classId && !a.Feat).All(Works);

    private static bool Works(AbilityDefinition a) =>
        Supported.Contains(a.Kind) && !a.NeedsRanged && !a.NeedsFirearm && !a.Reloads && !a.SpendLoad;

    /// <summary>The hero's abilities, in bar order: the class's own as their level comes, then those feats have given.</summary>
    public IReadOnlyList<AbilityDefinition> Bar
    {
        get
        {
            var c = Hero.Character;
            var own = session.Content.Abilities.Values.Where(a => a.Class == c.Class.Id && !a.Feat && a.Level <= c.Level)
                .OrderBy(a => SlotOrder(a.Slot));
            // Feats grant theirs from the ability's own level (a Bulwark's Shield Slam comes at 8).
            var granted = c.GrantedAbilities.Select(id => session.Content.Abilities.GetValueOrDefault(id)).OfType<AbilityDefinition>()
                .Where(a => a.Level <= c.Level);
            return own.Concat(granted).Where(Works).Select(c.Modify).ToList();
        }
    }

    private static int SlotOrder(string slot) => slot switch
    {
        "lmb" => 0,
        "rmb" => 1,
        _ => int.TryParse(slot, out var n) ? 1 + n : 99,
    };

    private int Rank => AbilityDefinition.SpellRank(Hero.Level);

    /// <summary>Seconds until an ability is ready again (0 when it is).</summary>
    public double CooldownLeft(AbilityDefinition a) => Math.Max(0, _readyAt.GetValueOrDefault(a.Id) - Clock);

    /// <summary>Why the hero can't use an ability on <paramref name="target"/> now, or null when they can.</summary>
    public string? WhyNot(AbilityDefinition a, Foe? target)
    {
        var resource = Hero.Resource;
        if (CooldownLeft(a) > 0)
            return $"{a.Name} is not ready ({CooldownLeft(a):0.#} s).";
        if (a.IsFlourish && Clock < _flourishReadyAt)
            return "Only one Flourish a round.";
        if (a.Cost > 0 && !resource.CanSpend(a.Cost))
            return $"{a.Name} needs {a.Cost:0} {resource.Name}.";
        if (a.NeedsShield && Hero.Pack.InSlot(EquipSlot.Shield) is null)
            return $"{a.Name} needs a shield.";
        if (a.NeedsTwoHanded && !Hero.Character.Weapon.Definition.Has(WeaponTrait.TwoHand))
            return $"{a.Name} needs a two-handed weapon.";
        if (a.Has("Spell") || a.Has("Cantrip") || a.IsSpell || a.IsSpellBurst)
        {
            if (Hero.Character.Conditions.Has(ConditionKind.Stupefied) && Hero.Character.Conditions.Value(ConditionKind.Stupefied) >= 4)
                return "You can't think clearly enough to cast.";
        }
        return WhyNotAim(a, target);
    }

    /// <summary>Why an ability can't reach <paramref name="target"/> (in reach, in range, a clear line), or null.</summary>
    private string? WhyNotAim(AbilityDefinition a, Foe? target)
    {
        if (!NeedsTarget(a))
            return null;
        if (target is null)
            return $"{a.Name} needs a monster in sight.";
        var distance = target.Monster.Position.DistanceTo(Hero.Position);
        return a.Kind switch
        {
            AbilityKind.WeaponStrike when target.Monster.Position.ChebyshevTo(Hero.Position) > 1 => $"{session.Called(target)} is out of reach.",
            AbilityKind.ChargeStrike when distance > a.Range + 0.5 => $"{session.Called(target)} is too far to charge.",
            AbilityKind.SpellBolt or AbilityKind.Missiles or AbilityKind.Blast when distance > RangeOf(a) + 0.5 => $"{session.Called(target)} is out of range.",
            _ when !ProjectionPath.Projectable(session.Level, Hero.Position, target.Monster.Position, (int)Math.Ceiling(Math.Max(1, RangeOf(a))) + 1)
                => $"You have no clear line to {session.Called(target)}.",
            _ => null,
        };
    }

    private static bool NeedsTarget(AbilityDefinition a) => a.Kind switch
    {
        AbilityKind.WeaponStrike or AbilityKind.ChargeStrike or AbilityKind.SpellBolt or AbilityKind.Missiles or AbilityKind.Blast => true,
        AbilityKind.Burst => a.Arc < 360,
        _ => false,
    };

    private double RangeOf(AbilityDefinition a) =>
        a.Kind == AbilityKind.WeaponStrike ? 1 : a.Range + (a.IsSpell || a.Has("Spell") ? Hero.Character.SpellRangeBonus : 0);

    /// <summary>The monster an ability goes for: the one at <paramref name="at"/>, else the nearest the hero sees.</summary>
    public Foe? TargetFor(AbilityDefinition a, Loc? at)
    {
        if (at is { } spot)
            return session.FoeAt(spot) is { } there && session.Seen(there) ? there : null;
        var seen = session.VisibleFoes().OrderBy(f => f.Monster.Position.DistanceTo(Hero.Position)).ToList();
        return a.Kind == AbilityKind.WeaponStrike
            ? seen.FirstOrDefault(f => f.Monster.Position.ChebyshevTo(Hero.Position) == 1)
            : seen.FirstOrDefault(f => WhyNot(a, f) is null) ?? seen.FirstOrDefault();
    }

    /// <summary>
    /// Uses an ability (by id) on the monster at <paramref name="at"/> (or the nearest one in sight). Returns the
    /// actions it took, or 0 when it couldn't be used (and why is said).
    /// </summary>
    public int Use(string id, Loc? at)
    {
        if (Bar.FirstOrDefault(a => a.Id == id) is not { } a)
        {
            session.Say("You don't know how to do that.");
            return 0;
        }
        return Unleash(a, at, pay: true);
    }

    /// <summary>
    /// Carries out an ability: paid for and put on its cooldown (with <paramref name="pay"/>; an artifact's activation
    /// has its own recharge instead). Returns the actions it took, or 0 when it couldn't be used.
    /// </summary>
    internal int Unleash(AbilityDefinition a, Loc? at, bool pay)
    {
        var target = NeedsTarget(a) ? TargetFor(a, at) : null;
        if ((pay ? WhyNot(a, target) : WhyNotAim(a, target)) is { } why)
        {
            session.Say(why);
            return 0;
        }
        if (!pay)
            return Carry(a, target, 0);
        var spent = a.SpendAll ? Hero.Resource.SpendAll() : a.Cost;
        if (!a.SpendAll && a.Cost > 0)
            Hero.Resource.TrySpend(a.Cost);
        if (a.Cooldown > 0)
            _readyAt[a.Id] = Clock + a.Cooldown;
        if (a.IsFlourish)
            _flourishReadyAt = Clock + FlourishLockoutSeconds;
        return Carry(a, target, spent);
    }

    private int Carry(AbilityDefinition a, Foe? target, double spent)
    {
        session.Publish(new AbilityUsedEvent(a.Id, a.Kind.ToString(), a.DamageType.ToString()));
        switch (a.Kind)
        {
            case AbilityKind.WeaponStrike:
                Strikes(a, target!, spent);
                break;
            case AbilityKind.ChargeStrike:
                Charge(a, target!);
                break;
            case AbilityKind.SpellBolt:
                Bolt(a, target!);
                break;
            case AbilityKind.Missiles:
                Missiles(a, target!);
                break;
            case AbilityKind.Burst:
                session.Say($"You use {a.Name}.");
                Burst(a, Hero.Position, target?.Monster.Position);
                break;
            case AbilityKind.Blast:
                session.Say($"You cast {a.Name}.");
                session.Publish(new SpellEvent(Hero.Position, target!.Monster.Position, MonsterSpellKind.Ball, ElementOf(a.DamageType), (int)Math.Round(a.Radius)));
                Burst(a, target.Monster.Position, null);
                break;
            case AbilityKind.Heal:
                var healed = (int)Math.Round(a.HealAt(Rank) * (1 + Hero.Character.HealShare));
                session.HealHero(healed);
                session.Say($"You use {a.Name} and feel better ({healed} HP).");
                break;
            case AbilityKind.Ward:
                RaiseWard(a);
                break;
            case AbilityKind.Aura:
                StartAura(a);
                break;
            case AbilityKind.Tumble:
                Tumble(a);
                break;
            case AbilityKind.Hide:
                Hide(a.BuffSeconds);
                break;
            case AbilityKind.Trap:
                _traps.Add(new LaidTrap(Hero.Position, a, Rank, Clock + Math.Max(5, a.BuffSeconds)));
                session.Say($"You set {a.Name} at your feet.");
                break;
        }
        return Math.Clamp(a.Actions, 1, 3);
    }

    // --- Weapon abilities ---

    private void Strikes(AbilityDefinition a, Foe target, double spent)
    {
        var options = OptionsFor(a, spent);
        var foes = new List<Foe> { target };
        if (a.Targets > 1)
        {
            // A sweep: every monster beside the hero within the arc, the target first.
            var toward = Angle(Hero.Position, target.Monster.Position);
            foes.AddRange(session.Foes.Where(f => f != target && f.Monster.Position.ChebyshevTo(Hero.Position) == 1
                    && (a.Arc >= 360 || AngleBetween(Angle(Hero.Position, f.Monster.Position), toward) <= a.Arc / 2))
                .Take(a.Targets - 1));
        }
        session.Say($"You use {a.Name}.");
        foreach (var foe in foes)
        {
            for (var i = 0; i < a.StrikeCount && foe.Monster.IsActive && !foe.Monster.IsRemoved; i++)
                session.Fight.HeroStrikes(foe, options);
        }
    }

    private StrikeOptions OptionsFor(AbilityDefinition a, double spent)
    {
        var level = Hero.Level;
        var persistent = a.WeaponPersistentAt(level) is { } dice ? (dice, a.DamageType) : ((Dice, DamageType)?)null;
        return new StrikeOptions(
            ExtraDice: a.ExtraDiceAt(level) + a.DiceForSpent(spent),
            OffGuard: a.Feint || a.Twin,
            SureCritical: Aura?.Ability.SureCritical == true,
            Drain: a.Drain,
            OnHit: HitFor(a),
            Persistent: persistent,
            Knockback: (int)Math.Round(a.Knockback));
    }

    private static OnHit? HitFor(AbilityDefinition a) =>
        a.OnHit is { } name && Enum.TryParse<ConditionKind>(name, out var kind) ? new OnHit(kind, Math.Max(1, a.OnHitValue), a.OnHitSeconds) : null;

    private void Charge(AbilityDefinition a, Foe target)
    {
        // Up to the ability's range toward the monster, free, then the Strike.
        for (var step = 0; step < (int)Math.Ceiling(a.Range) && target.Monster.Position.ChebyshevTo(Hero.Position) > 1; step++)
        {
            if (session.PathTo(target.Monster.Position) is not [var first, ..])
                break;
            var next = Hero.Position.Step(first);
            if (!session.Level.IsPassable(next) || session.FoeAt(next) is not null)
                break;
            session.MoveHero(next);
        }
        session.Say($"You use {a.Name}.");
        if (target.Monster.Position.ChebyshevTo(Hero.Position) == 1)
            session.Fight.HeroStrikes(target, OptionsFor(a, 0));
    }

    // --- Spells ---

    private void Bolt(AbilityDefinition a, Foe target)
    {
        session.Say($"You cast {a.Name}.");
        var struck = new List<Foe>();
        var foe = target;
        for (var jump = 0; foe is not null && jump <= a.Chain; jump++)
        {
            struck.Add(foe);
            session.Publish(new SpellEvent(jump == 0 ? Hero.Position : struck[^2].Monster.Position, foe.Monster.Position, MonsterSpellKind.Bolt, ElementOf(a.DamageType), 0));
            SpellAttack(a, foe, jump == 0);
            var from = foe.Monster.Position;
            foe = session.Foes.Where(f => !struck.Contains(f) && f.Monster.IsActive && f.Monster.Position.DistanceTo(from) <= 3 && session.Seen(f))
                .OrderBy(f => f.Monster.Position.DistanceTo(from)).FirstOrDefault();
        }
    }

    private void SpellAttack(AbilityDefinition a, Foe foe, bool first)
    {
        var hero = Hero;
        var attack = a.Has("Attack");
        var penalty = attack && first ? hero.Round.AttackPenalty(agile: false) : 0;
        if (attack && first)
            hero.Round.Strike();
        var check = Check.Roll(Roller, hero.Character.SpellAttack().Total + penalty, foe.ArmorClass().Total);
        var name = session.Seen(foe) ? session.Object(foe.Monster) : "it";
        var roll = $"({check.Natural} + {check.Modifier} = {check.Total} vs AC {check.Dc})";
        if (check.Degree < DegreeOfSuccess.Success)
        {
            session.Say($"{a.Name} misses {name} {roll}.");
            foe.Monster.Sleep = 0;
            foe.Monster.Aware = true;
            return;
        }
        var critical = check.Degree == DegreeOfSuccess.CriticalSuccess;
        var rolled = a.DamageAt(Rank).Roll(Roller);
        var damage = DamageCalculator.Compute(rolled, 0, check.Degree, a.DamageType, foe.Defenses);
        session.Say($"{a.Name} {(critical ? "critically hits" : "hits")} {name} {roll}: {damage} {Lower(a.DamageType)}.");
        hero.Resource.OnCombat(critical);
        Harm(a, foe, damage, critical, onHit: true);
        if (critical && a.CritPersistent is { } text && foe.Monster.IsActive && !foe.Monster.IsRemoved)
        {
            var dice = Dice.Parse(text);
            foe.Persistent.Add(a.DamageType, dice with { Count = dice.Count + Math.Max(0, Rank - 1) });
        }
    }

    private void Missiles(AbilityDefinition a, Foe target)
    {
        session.Say($"You cast {a.Name}.");
        var count = a.MissilesAt(Rank);
        for (var i = 0; i < count && target.Monster.IsActive && !target.Monster.IsRemoved; i++)
        {
            session.Publish(new SpellEvent(Hero.Position, target.Monster.Position, MonsterSpellKind.Bolt, ElementOf(a.DamageType), 0));
            var damage = DamageCalculator.ApplyDefenses(Math.Max(1, a.DamageAt(Rank).Roll(Roller)), a.DamageType, target.Defenses);
            session.Say($"A missile strikes {(session.Seen(target) ? session.Object(target.Monster) : "it")}: {damage} {Lower(a.DamageType)}.");
            Harm(a, target, damage, false, onHit: i == 0);
        }
    }

    /// <summary>
    /// A burst round <paramref name="centre"/> (a cone toward <paramref name="toward"/> when the ability has an arc): every
    /// monster in it makes a basic save against the spell DC (a spell) or the class DC.
    /// </summary>
    private void Burst(AbilityDefinition a, Loc centre, Loc? toward, int? rank = null)
    {
        var dc = a.IsSpellBurst || a.Has("Spell") ? Hero.Character.SpellDc().Total : Hero.Character.ClassDc().Total;
        var save = a.Save is { } s && Enum.TryParse<StatisticKind>(s, out var kind) ? kind : StatisticKind.Reflex;
        var heading = toward is { } t ? Angle(centre, t) : 0;
        var radius = Math.Max(1, a.Radius);
        var caught = session.Foes.Where(f => f.Monster.IsActive && f.Monster.Position.DistanceTo(centre) <= radius + 0.01
                && (toward is null || a.Arc >= 360 || AngleBetween(Angle(centre, f.Monster.Position), heading) <= a.Arc / 2 + 1)
                && ProjectionPath.Projectable(session.Level, centre, f.Monster.Position, (int)Math.Ceiling(radius) + 1))
            .ToList();
        if (a.Kind == AbilityKind.Burst && caught.Count > 0)
            session.Publish(new SpellEvent(centre, toward ?? centre, MonsterSpellKind.Breath, ElementOf(a.DamageType), (int)Math.Round(radius)));
        var hit = HitFor(a);
        foreach (var foe in caught)
        {
            var check = Check.Roll(Roller, SaveOf(foe, save), dc);
            var name = session.Seen(foe) ? session.Subject(foe.Monster) : "It";
            var harms = a.Dice is not null || a.WeaponDamage;
            if (harms)
            {
                var rolled = a.WeaponDamage
                    ? new Dice(Hero.Character.Weapon.DiceCount + a.ExtraDiceAt(Hero.Level), Hero.Character.Weapon.Definition.DamageDie).Roll(Roller)
                      + Hero.Character.DamageBonus().Total
                    : a.DamageAt(rank ?? Rank).Roll(Roller);
                var type = a.WeaponDamage ? Hero.Character.Weapon.Definition.DamageType : a.DamageType;
                var damage = DamageCalculator.BasicSave(rolled, check.Degree, type, foe.Defenses);
                session.Say($"{name} takes {damage} {Lower(type)} ({save} {check.Natural} + {check.Modifier} = {check.Total} vs DC {dc}: {Degree(check.Degree)}).");
                Harm(a, foe, damage, false, onHit: false);
            }
            else
            {
                session.Say($"{name} {(check.Degree >= DegreeOfSuccess.Success ? "shrugs it off" : "falters")} ({save} {check.Natural} + {check.Modifier} = {check.Total} vs DC {dc}).");
                foe.Monster.Sleep = 0;
                foe.Monster.Aware = true;
            }
            if (check.Degree <= DegreeOfSuccess.Failure && foe.Monster.IsActive && !foe.Monster.IsRemoved)
                AfterHit(foe, hit, a.WeaponPersistentAt(Hero.Level) is { } p ? (p, a.DamageType) : null, (int)Math.Round(a.Knockback),
                    check.Degree == DegreeOfSuccess.CriticalFailure, from: centre);
        }
        if (a.HideSeconds > 0)
            Hide(a.HideSeconds);
    }

    private static int SaveOf(Foe foe, StatisticKind save) =>
        save switch
        {
            StatisticKind.Fortitude => foe.Stats.Fortitude,
            StatisticKind.Will => foe.Stats.Will,
            _ => foe.Stats.Reflex,
        } - foe.Conditions.Value(ConditionKind.Frightened) - foe.Conditions.Value(ConditionKind.Sickened);

    /// <summary>Damage an ability deals, with its drain; a bolt's or missile's condition with it.</summary>
    private void Harm(AbilityDefinition a, Foe foe, int damage, bool critical, bool onHit)
    {
        if (a.Drain > 0 && damage > 0)
            session.HealHero((int)Math.Round(damage * a.Drain));
        session.Fight.Hurt(foe, damage);
        if (onHit && foe.Monster.IsActive && !foe.Monster.IsRemoved)
            AfterHit(foe, HitFor(a), null, (int)Math.Round(a.Knockback), critical);
    }

    /// <summary>What a hit or a failed save leaves on a monster: a condition, persistent damage, a shove away.</summary>
    internal void AfterHit(Foe foe, OnHit? hit, (Dice Dice, DamageType Type)? persistent, int knockback, bool critical, Loc? from = null)
    {
        if (hit is { } h)
            Afflict(foe, h.Kind, h.Value + (critical && h.Kind is ConditionKind.Frightened or ConditionKind.Sickened or ConditionKind.Enfeebled ? 1 : 0), h.Seconds);
        if (persistent is { } p)
            foe.Persistent.Add(p.Type, critical ? p.Dice with { Count = p.Dice.Count * 2 } : p.Dice);
        if (knockback > 0)
            Shove(foe, from ?? Hero.Position, knockback);
    }

    /// <summary>
    /// A condition on a monster, with what it means to an Angband monster: slowed is Angband's slow, frightened makes it
    /// lose its nerve, and prone, grabbed and immobilized hold it where it is (<see cref="ConditionSet.CanMove"/>).
    /// </summary>
    internal void Afflict(Foe foe, ConditionKind kind, int value, double seconds)
    {
        if (foe.Monster.Race.IsUnique && kind is ConditionKind.Stunned && foe.Monster.Race.Has(MonsterFlags.Questor))
            return;
        foe.Conditions.Apply(kind, value, seconds > 0 ? seconds : null);
        var turns = (int)Math.Ceiling(Math.Max(seconds, 2) / (EnergyTable.GameTurnsPerNormalTurn * DungeonSession.SecondsPerGameTurn));
        var name = session.Seen(foe) ? session.Subject(foe.Monster) : "It";
        switch (kind)
        {
            case ConditionKind.Slowed:
                foe.Monster.Slow = Math.Max(foe.Monster.Slow, turns);
                break;
            case ConditionKind.Frightened when !foe.Monster.Race.Has(MonsterFlags.NoFear):
                foe.Monster.Fear = Math.Max(foe.Monster.Fear, 2 * value);
                break;
        }
        session.Say($"{name} is {kind.ToString().ToLowerInvariant()}{(kind is ConditionKind.Frightened or ConditionKind.Sickened or ConditionKind.Enfeebled or ConditionKind.Stunned ? $" {value}" : "")}.");
    }

    private void Shove(Foe foe, Loc from, int tiles)
    {
        var at = foe.Monster.Position;
        var step = new Loc(Math.Sign(at.X - from.X), Math.Sign(at.Y - from.Y));
        if (step == new Loc(0, 0))
            return;
        for (var i = 0; i < tiles; i++)
        {
            var next = foe.Monster.Position + step;
            if (!session.Level.IsEmptyFloor(next) || next == Hero.Position)
                break;
            session.MoveMonster(foe.Monster, next);
        }
    }

    // --- Wards, auras, moving and hiding ---

    private void RaiseWard(AbilityDefinition a)
    {
        int ac, hardness;
        if (a.NeedsShield && Hero.Pack.InSlot(EquipSlot.Shield) is { } shield)
        {
            ac = 2;
            hardness = (session.ItemBridge.ShieldFor(shield)?.Hardness ?? 3) + Hero.Character.BlockHardnessBonus;
        }
        else
        {
            ac = Math.Max(1, a.WardAc);
            hardness = a.HardnessAt(Rank);
        }
        Ward = new ActiveWard(a, ac, hardness, a.WardSeconds > 0 ? a.WardSeconds : 6);
        Hero.Character.RemoveModifiers(WardSource);
        Hero.Character.AddModifier(StatisticKind.ArmorClass, new Modifier(WardSource, ac, BonusType.Circumstance));
        session.Say($"You use {a.Name} (+{ac} AC{(hardness > 0 ? $", soaks {hardness}" : "")}).");
    }

    private const string WardSource = "Ward";
    private const string AuraSource = "Aura";

    private void StartAura(AbilityDefinition a)
    {
        EndAura();
        Aura = new ActiveAura(a, Rank, Math.Max(2, a.BuffSeconds));
        if (a.AuraAc != 0)
        {
            foreach (var stat in new[] { StatisticKind.ArmorClass, StatisticKind.Fortitude, StatisticKind.Reflex, StatisticKind.Will })
                Hero.Character.AddModifier(stat, new Modifier(AuraSource, a.AuraAc, BonusType.Status));
        }
        if (a.AttackRate > 1)
            Hero.Timed["haste"] = Math.Max(Hero.Timed.GetValueOrDefault("haste"), (int)Math.Ceiling(a.BuffSeconds * 2));
        session.Say($"You use {a.Name}.");
    }

    private void EndAura()
    {
        if (Aura is null)
            return;
        Hero.Character.RemoveModifiers(AuraSource);
        Aura = null;
    }

    private void Tumble(AbilityDefinition a)
    {
        // To the open square within reach that is furthest from the nearest monster.
        var reach = Math.Max(1, (int)Math.Round(a.Range));
        var foes = session.Foes.Where(f => session.Seen(f)).Select(f => f.Monster.Position).ToList();
        var start = Hero.Position;
        var best = session.Level.AllLocs()
            .Where(p => p != start && p.DistanceTo(start) <= reach && session.Level.IsEmptyFloor(p)
                        && ProjectionPath.Projectable(session.Level, start, p, reach + 1))
            .OrderByDescending(p => foes.Count == 0 ? p.DistanceTo(start) : foes.Min(f => f.DistanceTo(p)))
            .Select(p => (Loc?)p).FirstOrDefault();
        if (best is { } to)
            session.MoveHero(to);
        session.Say($"You use {a.Name}.");
        if (a.HideSeconds > 0)
            Hide(a.HideSeconds);
    }

    /// <summary>
    /// Into the shadows: every monster not beside the hero that is hunting them loses them unless it beats a Stealth
    /// check (its Perception against the hero's Stealth DC). The first blow out of hiding finds its target off-guard.
    /// </summary>
    private void Hide(double seconds)
    {
        _hiddenUntil = Clock + Math.Max(2, seconds);
        var stealthDc = 10 + Hero.Character.Skill("Stealth").Total;
        var lost = 0;
        foreach (var foe in session.Foes.Where(f => f.Monster.Aware && f.Monster.Position.ChebyshevTo(Hero.Position) > 1))
        {
            if (Check.Roll(Roller, foe.Stats.Perception, stealthDc).Degree >= DegreeOfSuccess.Success)
                continue;
            foe.Monster.Aware = false;
            lost++;
        }
        session.Say(lost > 0 ? "You slip into the shadows, and they lose you." : "You slip into the shadows.");
    }

    /// <summary>The hero strikes from hiding: true once, and the hiding is over.</summary>
    internal bool ComeOutOfHiding()
    {
        if (!Hidden)
            return false;
        _hiddenUntil = 0;
        return true;
    }

    // --- What happens as the hero is hit ---

    /// <summary>How much of a blow a ward (its hardness, once, then a lockout) and an aura (its soak) take off.</summary>
    internal int Soak(int damage)
    {
        if (damage <= 0)
            return 0;
        var soaked = 0;
        if (Ward is { Hardness: > 0 } ward && Clock >= _blockReadyAt)
        {
            soaked += Math.Min(damage, ward.Hardness);
            _blockReadyAt = Clock + Math.Max(1, ward.Ability.BlockLockout);
        }
        if (Aura is { } aura && aura.Ability.AuraSoakAt(aura.Rank) is var soak and > 0)
            soaked += Math.Min(damage - soaked, soak);
        return Math.Max(0, soaked);
    }

    /// <summary>An aura that answers blows (Retributive Strike, Bastion): a Strike back, with the hero's reaction.</summary>
    internal void Retaliate(Foe foe)
    {
        if (Aura is not { Ability.Retaliate: true } aura || !Hero.Round.ReactionReady || foe.Monster.Position.ChebyshevTo(Hero.Position) > 1)
            return;
        Hero.Round.UseReaction();
        session.Say("You strike back!");
        session.Fight.HeroStrikes(foe, new StrikeOptions(ExtraDice: aura.Ability.ExtraDiceAt(Hero.Level)));
    }

    // --- The clock ---

    /// <summary>Time passes (every world tick): the resource, wards, auras, hiding and traps.</summary>
    internal void Tick(double seconds)
    {
        Clock += seconds;
        Hero.Resource.Update(seconds);
        if (Ward is { } ward && (ward.Remaining -= seconds) <= 0)
        {
            Ward = null;
            Hero.Character.RemoveModifiers(WardSource);
        }
        if (Aura is { } aura)
        {
            aura.Remaining -= seconds;
            aura.Pulse += seconds;
            if (aura.Ability.Steadfast)
            {
                foreach (var kind in new[] { ConditionKind.Frightened, ConditionKind.Stunned, ConditionKind.Prone, ConditionKind.Slowed })
                    Hero.Character.Conditions.Remove(kind);
            }
            while (aura.Pulse >= AuraPulseSeconds && !session.IsOver)
            {
                aura.Pulse -= AuraPulseSeconds;
                Pulse(aura);
            }
            if (aura.Remaining <= 0)
            {
                EndAura();
                session.Say($"{aura.Ability.Name} fades.");
            }
        }
        foreach (var trap in _traps.ToList())
        {
            if (Clock > trap.Expires)
            {
                _traps.Remove(trap);
                continue;
            }
            var radius = Math.Max(1.5, trap.Ability.Radius);
            if (session.Foes.Any(f => f.Monster.IsActive && f.Monster.Position.DistanceTo(trap.At) <= radius))
            {
                _traps.Remove(trap);
                session.Say($"{trap.Ability.Name} goes off!");
                Burst(trap.Ability, trap.At, null, trap.Rank);
            }
        }
    }

    private void Pulse(ActiveAura aura)
    {
        var a = aura.Ability;
        if (a.AuraHealAt(aura.Rank) is var heal and > 0)
            session.HealHero(heal);
        if (a.RegenShare > 0)
            session.HealHero((int)Math.Max(1, Math.Round(Hero.MaxHitPoints * a.RegenShare * AuraPulseSeconds)));
        if (a.Dice is null)
            return;
        var save = a.Save is { } s && Enum.TryParse<StatisticKind>(s, out var kind) ? kind : StatisticKind.Fortitude;
        var dc = Hero.Character.ClassDc().Total;
        var hit = HitFor(a);
        foreach (var foe in session.Foes.Where(f => f.Monster.IsActive && f.Monster.Position.DistanceTo(Hero.Position) <= Math.Max(1, a.Radius)).ToList())
        {
            var type = a.Holy ? (foe.Monster.Race.Has(MonsterFlags.Undead) ? DamageType.Vitality : DamageType.Fire) : a.DamageType;
            var check = Check.Roll(Roller, SaveOf(foe, save), dc);
            var damage = DamageCalculator.BasicSave(a.AuraDiceAt(aura.Rank).Roll(Roller), check.Degree, type, foe.Defenses);
            if (damage > 0 && session.Seen(foe))
                session.Say($"Your {a.Name} burns {session.Object(foe.Monster)}: {damage} {Lower(type)}.");
            Harm(a, foe, damage, false, onHit: false);
            if (check.Degree <= DegreeOfSuccess.Failure && hit is not null && foe.Monster.IsActive && !foe.Monster.IsRemoved)
                AfterHit(foe, hit, null, 0, check.Degree == DegreeOfSuccess.CriticalFailure);
        }
    }

    // --- Helpers ---

    private static double Angle(Loc from, Loc to) => Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;

    private static double AngleBetween(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static string Lower(DamageType type) => type.ToString().ToLowerInvariant();

    private static string Degree(DegreeOfSuccess degree) => degree switch
    {
        DegreeOfSuccess.CriticalSuccess => "critical success",
        DegreeOfSuccess.Success => "success",
        DegreeOfSuccess.Failure => "failure",
        _ => "critical failure",
    };

    /// <summary>The Angband element a spell's flash is drawn as.</summary>
    private static string? ElementOf(DamageType type) => type switch
    {
        DamageType.Fire => "fire",
        DamageType.Cold => "cold",
        DamageType.Electricity => "elec",
        DamageType.Acid => "acid",
        DamageType.Poison => "pois",
        DamageType.Void => "nether",
        DamageType.Vitality or DamageType.Spirit => "light",
        DamageType.Force => "force",
        DamageType.Sonic => "sound",
        _ => null,
    };
}

/// <summary>A ward up: its AC, the hardness it takes off a blow, and the seconds it has left.</summary>
public sealed class ActiveWard(AbilityDefinition ability, int ac, int hardness, double remaining)
{
    public AbilityDefinition Ability { get; } = ability;
    public int Ac { get; } = ac;
    public int Hardness { get; } = hardness;
    public double Remaining { get; set; } = remaining;
}

/// <summary>An aura or stance about the hero: its ability, rank, the seconds it has left, and time toward its next pulse.</summary>
public sealed class ActiveAura(AbilityDefinition ability, int rank, double remaining)
{
    public AbilityDefinition Ability { get; } = ability;
    public int Rank { get; } = rank;
    public double Remaining { get; set; } = remaining;
    public double Pulse { get; set; }
}

/// <summary>A trap the hero has set: where, which, its rank, and when it falls apart.</summary>
public sealed record LaidTrap(Loc At, AbilityDefinition Ability, int Rank, double Expires);
