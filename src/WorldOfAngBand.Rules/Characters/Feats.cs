using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Characters;

public enum FeatType
{
    Ancestry,
    Class,
    Skill,
    General,
    /// <summary>A side story's permanent reward, granted by a dialogue effect (never chosen on the level-up screen).</summary>
    Boon,
    /// <summary>A class's subclass (spec, section 7): chosen at level 1, its feature and ability; one per character.</summary>
    Subclass,
}

/// <summary>
/// What a feat does, read from <c>feats.json</c>. Each field is one kind of effect; a feat lists as many as it needs:
/// <list type="bullet">
/// <item><see cref="Statistic"/> + <see cref="Value"/>: an untyped bonus to a statistic (Will, Perception, ArmorClass ...).</item>
/// <item><see cref="Skill"/> + <see cref="Value"/>: a circumstance bonus to a skill's checks ("Lore" means any Lore,
/// "Untrained" any untrained skill).</item>
/// <item><see cref="HitPointsPerLevel"/>, <see cref="SpeedFeet"/>, <see cref="Resist"/> + <see cref="Value"/>.</item>
/// <item><see cref="Recovery"/>: out-of-combat recovery faster by this share (0.5 = half as fast again).</item>
/// <item><see cref="TaintRate"/>: Taint builds more slowly by this share.</item>
/// <item><see cref="CritDamage"/>: extra damage on a critical hit. <see cref="FuryPerHit"/>: extra Fury on each hit.</item>
/// <item><see cref="ResourceRegen"/>: Mana regenerates faster by this share.</item>
/// <item><see cref="Reach"/>: melee reach, in tiles. <see cref="SpellRange"/>: spell range, in tiles.</item>
/// <item><see cref="Ability"/>: a new ability on the bar. <see cref="ExtraMissiles"/> on <see cref="Ability"/>: one more missile.</item>
/// <item><see cref="IgnoreArmorSpeed"/>: body armour never slows the character.</item>
/// <item>An <see cref="Ability"/> with any of <see cref="Targets"/>, <see cref="Cost"/>, <see cref="Knockback"/>,
/// <see cref="Chain"/>, <see cref="Splash"/>, <see cref="Radius"/>, <see cref="OnHitSeconds"/> or
/// <see cref="WardSeconds"/>, <see cref="BuffSeconds"/>, <see cref="ExtraDice"/> or <see cref="Cooldown"/> set doesn't add the ability: it changes it wherever it is on the bar (the spec's
/// "modifier" feats: one more target, 5 less Fury, a longer ward).</item>
/// <item><see cref="BlockHardness"/>: Shield Block with a shield takes this much more off a blow.
/// <see cref="BlockCounter"/>: after a Shield Block the hero strikes back at the attacker if it is in reach.</item>
/// <item>The Rogue's: <see cref="SneakDice"/> more sneak attack dice; <see cref="AmbushCrit"/>: a Strike out of hiding
/// that hits is a critical hit; <see cref="SwiftSneak"/>: no slower while hidden; <see cref="TumbleFeint"/>: enemies
/// next to a Tumble's path are left off-guard. <see cref="PersistentDice"/> on an <see cref="Ability"/>: its wound or poison
/// deals more dice.</item>
/// <item>The Ranger's: <see cref="Pierce"/> (on its own: every arrow passes through that many more enemies; on an
/// <see cref="Ability"/>: that ability's arrows do), <see cref="CritResource"/> back on a critical hit with a bow,
/// <see cref="WeaponRange"/> more tiles for a bow, <see cref="PreyDice"/> more Hunt Prey dice. <see cref="Companion"/>:
/// an animal companion (a creature id) fights beside the hero; <see cref="CompanionGrowth"/> makes it stronger.
/// <see cref="Pet"/>: a creature that follows any hero as well as a companion (a boon's: the drakeling).</item>
/// <item>The Champion's: <see cref="UndeadBane"/> vitality damage on Strikes against the undead; <see cref="HealShare"/>:
/// Lay on Hands heals that share more; <see cref="Light"/> tiles of light round the hero; <see cref="TaintFuel"/>: the
/// Oathbreaker's hits fill the Taint meter, and its Strikes drain life by how full it is; <see cref="Retribution"/>: the
/// Paladin's reaction is always ready, striking back at a monster that hits the hero or an ally beside them.</item>
/// <item>The Gunslinger's: <see cref="ReloadSpeed"/> (reloads take that share less time), <see cref="Capacity"/> (every
/// gun holds that many rounds more), <see cref="CritReload"/> (a critical hit with a gun puts a round back),
/// <see cref="DistantDamage"/> (more damage with a gun at a target 6 tiles off or more: the Sniper's), and
/// <see cref="RoundsBonus"/> (one more special round from each casting at the forge).</item>
/// <item>The Necromancer's: <see cref="Minions"/> more skeletons at once, <see cref="MinionGrowth"/> makes them sturdier,
/// <see cref="GraveCharges"/> more Grave Charges held, <see cref="ChargesPerCorpse"/> more from each corpse.</item>
/// </list>
/// </summary>
public sealed record FeatEffect(
    string? Statistic = null,
    int Value = 0,
    string? Skill = null,
    int HitPointsPerLevel = 0,
    int SpeedFeet = 0,
    DamageType? Resist = null,
    double Recovery = 0,
    double TaintRate = 0,
    int CritDamage = 0,
    int FuryPerHit = 0,
    double ResourceRegen = 0,
    double Reach = 0,
    double SpellRange = 0,
    string? Ability = null,
    int ExtraMissiles = 0,
    bool IgnoreArmorSpeed = false,
    int Targets = 0,
    double Cost = 0,
    double Knockback = 0,
    int Chain = 0,
    int Splash = 0,
    double Radius = 0,
    double OnHitSeconds = 0,
    double WardSeconds = 0,
    int BlockHardness = 0,
    bool BlockCounter = false,
    int SneakDice = 0,
    bool AmbushCrit = false,
    bool SwiftSneak = false,
    bool TumbleFeint = false,
    int PersistentDice = 0,
    int Pierce = 0,
    double CritResource = 0,
    double WeaponRange = 0,
    int PreyDice = 0,
    string? Companion = null,
    int CompanionGrowth = 0,
    int UndeadBane = 0,
    double HealShare = 0,
    double Light = 0,
    bool TaintFuel = false,
    bool Retribution = false,
    int Minions = 0,
    int MinionGrowth = 0,
    int GraveCharges = 0,
    int ChargesPerCorpse = 0,
    double ReloadSpeed = 0,
    int Capacity = 0,
    bool CritReload = false,
    int DistantDamage = 0,
    int RoundsBonus = 0,
    double BuffSeconds = 0,
    int ExtraDice = 0,
    double Cooldown = 0,
    string? Pet = null)
{
    /// <summary>Whether this effect changes an ability already on the bar rather than adding one.</summary>
    public bool ModifiesAbility => Ability is not null && (ExtraMissiles != 0 || Targets != 0 || Cost != 0 || Knockback != 0
        || Chain != 0 || Splash != 0 || Radius != 0 || OnHitSeconds != 0 || WardSeconds != 0 || PersistentDice != 0 || Pierce != 0
        || BuffSeconds != 0 || ExtraDice != 0 || Cooldown != 0);
}

/// <summary>
/// A feat: its type and the level it becomes available at, who may take it (an ancestry, a class, a subclass, training
/// in a skill) and its effects. A background's skill feat is granted at level 1 by name.
/// </summary>
/// <summary>
/// A feat, read from <c>feats.json</c>. A <see cref="Dedication"/> (a class id) is a multiclass dedication: a class feat
/// open to every other class, one per character, granting that class's ability (spec, section 7).
/// </summary>
public sealed record FeatDefinition(string Id, string Name, FeatType Type, int Level, string Description, IReadOnlyList<FeatEffect> Effects,
    string? Ancestry = null, string? Class = null, string? Skill = null, string? Subclass = null, string? Dedication = null);

/// <summary>
/// What a character picked on reaching a level: the feats and skill increase the level grants (see
/// <see cref="Progression.LevelProgression"/>), and four attribute boosts at 5, 10, 15 and 20.
/// </summary>
public sealed record LevelChoice(
    AttributeKind[]? Boosts = null,
    string? ClassFeat = null,
    string? SkillFeat = null,
    string? GeneralFeat = null,
    string? AncestryFeat = null,
    string? SkillIncrease = null);
