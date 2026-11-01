using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Abilities;

/// <summary>How an ability does its work.</summary>
public enum AbilityKind
{
    /// <summary>A weapon Strike in melee reach (the basic attack, Power Attack).</summary>
    WeaponStrike,

    /// <summary>Dash up to <see cref="AbilityDefinition.Range"/> tiles to the target, then Strike (Sudden Charge).</summary>
    ChargeStrike,

    /// <summary>A projectile that makes a spell attack roll on arrival (Ignition).</summary>
    SpellBolt,

    /// <summary>Projectiles that always hit (Force Bolt).</summary>
    Missiles,

    /// <summary>
    /// A ward on the hero, no target (Raise a Shield, the Shield cantrip): a circumstance bonus to AC for
    /// <see cref="AbilityDefinition.WardSeconds"/>, and a Shield Block that soaks its hardness from the next blow.
    /// </summary>
    Ward,

    /// <summary>A thrown alchemical bomb from the belt: an attack roll on arrival, then splash around the target.</summary>
    Bomb,

    /// <summary>An elixir from the belt, drunk at once (an antidote).</summary>
    Elixir,

    /// <summary>
    /// An area round the hero: every enemy within <see cref="AbilityDefinition.Radius"/> (and <see cref="AbilityDefinition.Arc"/>
    /// of the target's direction, for a cone) makes a basic save against the hero's spell DC (a spell) or class DC.
    /// A full burst needs no target (Thunderclap, Earthshaker, Challenge); a cone aims at one (Burning Hands).
    /// </summary>
    Burst,

    /// <summary>A roll up to <see cref="AbilityDefinition.Range"/> tiles toward the pointer, untouchable on the way (the Rogue's Tumble).</summary>
    Tumble,

    /// <summary>Marks the target, at once and from up to <see cref="AbilityDefinition.Range"/> tiles (the Ranger's Hunt Prey).</summary>
    Mark,

    /// <summary>
    /// Sends the hero's companion at the target (the Beastmaster's Sic 'Em): it bounds there and its next bite has
    /// <see cref="AbilityDefinition.ExtraDice"/> more dice and the ability's <see cref="AbilityDefinition.OnHit"/>.
    /// </summary>
    Command,

    /// <summary>
    /// Into the shadows for <see cref="AbilityDefinition.BuffSeconds"/> or until the hero attacks (the Rogue's Hide in
    /// Shadows): unaware monsters notice the hero only close by, hunting ones may lose them, and the first blow out of
    /// hiding finds its target off-guard.
    /// </summary>
    Hide,

    /// <summary>
    /// A trap set at the hero's feet (the Trickster's Snare): it arms, and the first enemy to step close sets it off,
    /// a burst of <see cref="AbilityDefinition.Radius"/> round it (basic save, damage, the condition on a failure).
    /// </summary>
    Trap,

    /// <summary>
    /// An aura round the hero for <see cref="AbilityDefinition.BuffSeconds"/> (the Champion's): every
    /// <see cref="GameWorld.AuraTickSeconds"/> the enemies within <see cref="AbilityDefinition.Radius"/> take its
    /// <see cref="AbilityDefinition.Dice"/> (a basic save, with its <see cref="AbilityDefinition.OnHit"/> on a failure)
    /// and the hero and allies within it heal <see cref="AbilityDefinition.AuraHeal"/>; while it lasts the hero has
    /// <see cref="AbilityDefinition.AuraAc"/> to AC and saves and every blow on someone within it loses
    /// <see cref="AbilityDefinition.Soak"/>. Using it again renews it.
    /// </summary>
    Aura,

    /// <summary>
    /// Raises an ally beside the hero (the Necromancer's Raise Bones): a <see cref="AbilityDefinition.Creature"/> that
    /// fights for them until it falls. At the most the hero can hold, the oldest crumbles to make room.
    /// </summary>
    Summon,

    /// <summary>
    /// A burst centred on the target, from up to <see cref="AbilityDefinition.Range"/> tiles (the Plaguebringer's Corpse
    /// Explosion): every enemy within <see cref="AbilityDefinition.Radius"/> of it saves, as for <see cref="Burst"/>.
    /// </summary>
    Blast,

    /// <summary>Heals the hero, and the allies within <see cref="AbilityDefinition.Radius"/>, by <see cref="AbilityDefinition.Heal"/> a spell rank (Lay on Hands).</summary>
    Heal,
}

/// <summary>
/// An active ability, read from abilities.json. Costs are in the class resource (Fury, Mana). <see cref="Actions"/>
/// is the Pathfinder action cost, which becomes the wind-up time (0.5 s per action, never faster than a weapon swing
/// for weapon abilities). A class ability with a <see cref="Level"/> above 1 joins the bar at that level.
/// <list type="bullet">
/// <item><see cref="Targets"/> and <see cref="Arc"/>: a sweep strikes more enemies in reach, within this many degrees of
/// the target's direction (Cleave's 120 in front, Swipe's 360 all round). Only the main target's hit builds Fury.</item>
/// <item><see cref="SpendAll"/>: spends all the resource there is (at least <see cref="Cost"/>), one more weapon die per
/// <see cref="ResourcePerDie"/> spent (Finishing Blow).</item>
/// <item>Wards: <see cref="WardAc"/>, <see cref="WardHardness"/> (+<see cref="HardnessPerTwoRanks"/> every two spell
/// ranks above 1), <see cref="WardSeconds"/>, <see cref="NeedsShield"/> (the shield's own AC and hardness), and
/// <see cref="BlockLockout"/>: after it blocks, the ability can't be used again for that long (the Shield cantrip).</item>
/// <item>Belt items: <see cref="Splash"/> damage around a bomb's target, <see cref="PersistentDice"/> on a hit,
/// <see cref="Cures"/> persistent damage of a type, <see cref="ResistsPoison"/> for <see cref="BuffSeconds"/>.
/// A spell bolt with <see cref="Splash"/> splashes the enemies round its target too (the Pyromancer's Ignition).</item>
/// <item>Bursts: <see cref="Radius"/> in tiles, the <see cref="Save"/> the enemies make (Reflex, Fortitude, Will),
/// <see cref="WeaponDamage"/> (the weapon's dice and damage bonus instead of <see cref="Dice"/>); <see cref="OnHit"/>
/// and <see cref="Knockback"/> land on a failed save. <see cref="NeedsTwoHanded"/>: only with a two-handed weapon.</item>
/// <item>Rogue Strikes: <see cref="Feint"/> (the target is off-guard against it), <see cref="Twin"/> (two Strikes,
/// the second a feint), <see cref="PersistentDice"/> of <see cref="DamageType"/> on a weapon hit (Gutting Strike; one more die
/// at levels 5, 10 and 15). Traps: <see cref="BuffSeconds"/> is how long one lies set.</item>
/// <item>Weapon Strikes with a bow loose arrows: <see cref="Strikes"/> of them (Twin Shot), each passing through
/// <see cref="Pierce"/> more enemies behind its target (Piercing Shot); <see cref="NeedsRanged"/>: only with a bow.
/// A sweep (more than one target) needs a melee weapon.</item>
/// <item>The Champion's: <see cref="Heal"/> HP a spell rank (Lay on Hands); auras (<see cref="AbilityKind.Aura"/>) with
/// <see cref="AuraAc"/> (a status bonus to AC and saves), <see cref="Soak"/> (off every blow, one more every two ranks),
/// <see cref="AuraHeal"/> a tick (one more every two ranks), <see cref="Dice"/> a tick (a die more every two ranks),
/// <see cref="Drain"/> (the share of an aura's damage that heals the hero), <see cref="Holy"/> (vitality against the
/// undead, fire against the living), <see cref="RaisesShield"/> (a shield in hand is raised for as long) and
/// <see cref="CostsTaint"/> (its cost comes out of the Taint meter, not Devotion: the Oathbreaker's).</item>
/// <item>The Necromancer's: <see cref="Charges"/> Grave Charges spent as well as any <see cref="Cost"/>;
/// <see cref="ChargeInstead"/>: a Grave Charge pays for it when there is one, else its Mana (Raise Bones);
/// <see cref="Creature"/>: what a summon raises. A spell bolt with <see cref="Drain"/> heals the hero that share of its
/// damage (Siphon Life).</item>
/// <item>Past level 5 (M4): <see cref="HideSeconds"/> leaves the hero hidden that long after a Tumble or a self burst
/// (Shadowstep, Smoke Bomb); an aura with <see cref="Retaliate"/> strikes back at each monster that hits the hero in
/// reach, at most once a second (Retributive Strike); <see cref="CleansesTaint"/> takes that much off the Taint meter
/// (Beacon of Hope); a burst's <see cref="Drain"/> heals the hero that share of what it deals (Dread Harvest); a summon
/// raises <see cref="Count"/> allies, and with <see cref="BuffSeconds"/> they last only that long and don't count
/// against the hero's skeletons (Call of the Pack); <see cref="NeedsShield"/> on a Strike needs a shield in hand (Shield
/// Slam).</item>
/// <item>The Gunslinger's: <see cref="NeedsFirearm"/> (only with a gun in hand); <see cref="Reloads"/> (the gun is loaded
/// in an instant first: Risky Reload, Hit the Dirt!); <see cref="SpendLoad"/> (one Strike for every round loaded: Fan
/// the Hammer); <see cref="Round"/> (a special round from the belt, used up as it is fired: its <see cref="PersistentDice"/>
/// of <see cref="DamageType"/>, its <see cref="Dice"/> of vitality against the undead when <see cref="Holy"/>, or a
/// spread at <see cref="Targets"/> enemies within <see cref="Arc"/>). A gun's Strike at more than one target is a
/// spread of shot, not a sweep.</item>
/// <item>The level-16 capstones (M4 step 8) are auras that change the hero while they last: <see cref="AttackRate"/>
/// times as many blows (Unstoppable), <see cref="Steadfast"/> (no stun, hold, slow, fear, fall or song takes them),
/// <see cref="RegenShare"/> of their HP back a pulse (Avatar of the Lantern), allies dealing <see cref="MinionDamage"/>
/// more (Lich Form), every monster within <see cref="Radius"/> their prey (<see cref="AllPrey"/>: Hunter's Apex), the next
/// Strike that hits a critical hit (<see cref="SureCritical"/>: Death Mark, which it ends), and guns that never empty
/// (<see cref="FreeReloads"/>).</item>
/// </list>
/// </summary>
public sealed record AbilityDefinition(
    string Id,
    string Name,
    string Class,
    string Slot,
    AbilityKind Kind,
    int Actions = 1,
    double Range = 0,
    double Cost = 0,
    double Cooldown = 0,
    bool Repeat = false,
    int ExtraDice = 0,
    int CadenceAttacks = 1,
    string? Dice = null,
    string? HeightenDice = null,
    string? CritPersistent = null,
    int Missiles = 1,
    DamageType DamageType = DamageType.Bludgeoning,
    IReadOnlyList<string>? Traits = null,
    string Description = "",
    string? OnHit = null,
    int OnHitValue = 1,
    double OnHitSeconds = 0,
    double Knockback = 0,
    int Targets = 1,
    int Chain = 0,
    bool Feat = false,
    int Level = 1,
    double Arc = 360,
    bool SpendAll = false,
    double ResourcePerDie = 0,
    int WardAc = 0,
    int WardHardness = 0,
    int HardnessPerTwoRanks = 0,
    double WardSeconds = 0,
    bool NeedsShield = false,
    double BlockLockout = 0,
    int Splash = 0,
    string? PersistentDice = null,
    DamageType? Cures = null,
    int ResistsPoison = 0,
    double BuffSeconds = 0,
    double Radius = 0,
    string? Save = null,
    bool WeaponDamage = false,
    bool NeedsTwoHanded = false,
    bool Feint = false,
    bool Twin = false,
    int Strikes = 1,
    int Pierce = 0,
    bool NeedsRanged = false,
    int Heal = 0,
    int AuraAc = 0,
    int Soak = 0,
    int AuraHeal = 0,
    double Drain = 0,
    bool Holy = false,
    bool RaisesShield = false,
    bool CostsTaint = false,
    int Charges = 0,
    bool ChargeInstead = false,
    string? Creature = null,
    double HideSeconds = 0,
    bool Retaliate = false,
    int CleansesTaint = 0,
    int Count = 1,
    bool NeedsFirearm = false,
    bool Reloads = false,
    bool SpendLoad = false,
    bool Round = false,
    double AttackRate = 1.0,
    bool Steadfast = false,
    double RegenShare = 0,
    double MinionDamage = 0,
    bool AllPrey = false,
    bool SureCritical = false,
    bool FreeReloads = false)
{
    public bool Has(string trait) => Traits?.Contains(trait, StringComparer.OrdinalIgnoreCase) == true;

    public bool IsFlourish => Has("Flourish");

    public bool IsSpell => Kind is AbilityKind.SpellBolt or AbilityKind.Missiles;

    /// <summary>How many Strikes it makes at its target: two for a twin (Twin Feint, whose second is a feint), else <see cref="Strikes"/>.</summary>
    public int StrikeCount => Twin ? Math.Max(2, Strikes) : Math.Max(1, Strikes);

    /// <summary>A sweep: a Strike at more than one enemy (Cleave, Swipe), which needs a melee weapon.</summary>
    public bool IsSweep => Kind == AbilityKind.WeaponStrike && Targets > 1;

    public bool UsesWeapon => Kind is AbilityKind.WeaponStrike or AbilityKind.ChargeStrike || (Kind == AbilityKind.Burst && WeaponDamage);

    /// <summary>Used on the hero, with no target: wards, elixirs, hiding, traps, tumbles and full bursts round the hero.</summary>
    public bool IsSelf => Kind is AbilityKind.Ward or AbilityKind.Elixir or AbilityKind.Hide or AbilityKind.Trap or AbilityKind.Tumble
        or AbilityKind.Aura or AbilityKind.Heal or AbilityKind.Summon
        || (Kind == AbilityKind.Burst && Arc >= 360);

    /// <summary>A burst made with spell power (spell DC, cast speed) rather than a weapon or the class DC.</summary>
    public bool IsSpellBurst => Kind is AbilityKind.Burst or AbilityKind.Blast && Has("Spell");

    /// <summary>Thrown or drunk from the belt, using up one of the item.</summary>
    public bool IsConsumable => Kind is AbilityKind.Bomb or AbilityKind.Elixir || Round;

    /// <summary>A ward's hardness at a spell rank (the Shield cantrip's 5 grows by 5 every two ranks).</summary>
    public int HardnessAt(int rank) => WardHardness + HardnessPerTwoRanks * ((rank - 1) / 2);

    /// <summary>Finishing Blow's extra weapon dice for the resource spent.</summary>
    public int DiceForSpent(double spent) => ResourcePerDie > 0 ? (int)(spent / ResourcePerDie) : 0;

    /// <summary>A Pathfinder spell rank for a caster of <paramref name="level"/>: half the level, rounded up.</summary>
    public static int SpellRank(int level) => Math.Max(1, (level + 1) / 2);

    /// <summary>The damage dice at a spell rank: the base dice plus <see cref="HeightenDice"/> per rank above 1.</summary>
    public Dice DamageAt(int rank)
    {
        var dice = Dice is { } text ? Rules.Dice.Parse(text) : new Rules.Dice(0, 1);
        if (HeightenDice is { } extra && rank > 1)
        {
            var step = Rules.Dice.Parse(extra);
            dice = dice with { Count = dice.Count + step.Count * (rank - 1), Bonus = dice.Bonus + step.Bonus * (rank - 1) };
        }
        return dice;
    }

    /// <summary>Weapon abilities' extra dice grow at levels 10 and 18, like Power Attack.</summary>
    public int ExtraDiceAt(int level) => ExtraDice == 0 ? 0 : ExtraDice + (level >= 10 ? 1 : 0) + (level >= 18 ? 1 : 0);

    /// <summary>A weapon ability's persistent damage on a hit (Gutting Strike): its dice, one more at levels 5, 10 and 15.</summary>
    public Dice? WeaponPersistentAt(int level)
    {
        if (PersistentDice is not { } text)
            return null;
        var dice = Rules.Dice.Parse(text);
        return dice with { Count = dice.Count + (level >= 5 ? 1 : 0) + (level >= 10 ? 1 : 0) + (level >= 15 ? 1 : 0) };
    }

    /// <summary>Lay on Hands' healing at a spell rank: <see cref="Heal"/> a rank, as Pathfinder's 6 a rank.</summary>
    public int HealAt(int rank) => Heal * Math.Max(1, rank);

    /// <summary>An aura's dice a tick at a spell rank: one more die every two ranks above 1.</summary>
    public Dice AuraDiceAt(int rank)
    {
        var dice = Dice is { } text ? Rules.Dice.Parse(text) : new Rules.Dice(0, 1);
        return dice with { Count = dice.Count + (rank - 1) / 2 };
    }

    /// <summary>An aura's soak and healing at a spell rank: one more every two ranks above 1 (none without a base).</summary>
    public int AuraSoakAt(int rank) => Soak > 0 ? Soak + (rank - 1) / 2 : 0;
    public int AuraHealAt(int rank) => AuraHeal > 0 ? AuraHeal + (rank - 1) / 2 : 0;

    /// <summary>Force Bolt-style missile count: one more every two spell ranks.</summary>
    public int MissilesAt(int rank) => Missiles + (rank - 1) / 2;
}
