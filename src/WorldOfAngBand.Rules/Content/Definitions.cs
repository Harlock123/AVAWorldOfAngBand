using WorldOfAngBand.Rules.Items;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Content;

/// <summary>A playable ancestry (race). Speed is in feet: 25 ft is the reference run speed of 5 tiles a second.</summary>
public sealed record AncestryDefinition(
    string Id,
    string Name,
    int HitPoints,
    int SpeedFeet,
    IReadOnlyList<AttributeKind> Boosts,
    int FreeBoosts,
    IReadOnlyList<AttributeKind> Flaws,
    string Senses,
    string Trait,
    IReadOnlyList<HeritageDefinition>? Heritages = null,
    int FireResistancePerTwoLevels = 0);

/// <summary>
/// A heritage: the variety of an ancestry. The numeric fields are applied by the rules; the description also covers
/// effects that wait on later systems (skills, vision, extra cantrips).
/// </summary>
public sealed record HeritageDefinition(
    string Id,
    string Name,
    string Description,
    int HitPointsPerLevel = 0,
    int SpeedFeet = 0,
    int Will = 0,
    IReadOnlyDictionary<Combat.DamageType, int>? Resistances = null);

/// <summary>A background: one boost from <see cref="BoostChoices"/>, one free boost, a trained skill, a Lore and a skill feat.</summary>
public sealed record BackgroundDefinition(
    string Id,
    string Name,
    IReadOnlyList<AttributeKind> BoostChoices,
    string Skill,
    string Lore,
    string SkillFeat);

/// <summary>The statistics whose proficiency rank a class raises as it levels.</summary>
public enum ProficiencyTrack
{
    Attack,
    Spell,
    Armor,
    Fortitude,
    Reflex,
    Will,
    Perception,
    ClassDc,
}

/// <summary>
/// A class. <see cref="Proficiencies"/> maps each track to the levels where its rank changes, e.g.
/// Attack: { 1: Expert, 5: Master, 13: Legendary } for the Fighter.
/// </summary>
public sealed record ClassDefinition(
    string Id,
    string Name,
    int HitPointsPerLevel,
    IReadOnlyList<AttributeKind> KeyAttributes,
    string Resource,
    IReadOnlyList<string> Subclasses,
    IReadOnlyDictionary<ProficiencyTrack, IReadOnlyDictionary<int, ProficiencyRank>> Proficiencies,
    IReadOnlyList<ArmorCategory>? ArmorCategories = null,
    IReadOnlyList<string>? TrainedSkills = null,
    int SkillPicks = 0,
    bool SneakAttack = false,
    bool FinesseDamage = false,
    bool HuntsPrey = false,
    bool GraveCharges = false,
    string? Also = null,
    bool GunDamage = false)
{
    // GunDamage (the Gunslinger's Singular Expertise): +1 damage with a gun, +2 at level 7, +3 at 15.
    // Also: the name players may know the class by (the Champion is Pathfinder's paladin), shown beside its own.
    // GraveCharges (the Necromancer): monsters dying near the hero leave Grave Charges, spent on its grave magic.

    /// <summary>Hunt Prey's precision dice at a level (the Ranger, <see cref="HuntsPrey"/>): 1d8, 2d8 at 11, 3d8 at 19, as Pathfinder's precision edge.</summary>
    public int PreyDiceAt(int level) => HuntsPrey ? 1 + (level >= 11 ? 1 : 0) + (level >= 19 ? 1 : 0) : 0;

    // FinesseDamage (the Rogue): DEX instead of STR to damage with a finesse weapon, as Pathfinder's thief.

    /// <summary>
    /// Sneak attack's precision dice at a level (the Rogue, <see cref="SneakAttack"/>): 1d6, one more at 5, 11 and 17,
    /// as in Pathfinder. None for other classes.
    /// </summary>
    public int SneakAttackDiceAt(int level) =>
        SneakAttack ? 1 + (level >= 5 ? 1 : 0) + (level >= 11 ? 1 : 0) + (level >= 17 ? 1 : 0) : 0;

    /// <summary>
    /// Whether the class is trained in an armour category. Unarmoured defence always is; in armour it isn't trained
    /// in, the character gets no proficiency bonus to AC, as in Pathfinder. Null means every category.
    /// </summary>
    public bool IsTrainedIn(ArmorCategory category) =>
        category == ArmorCategory.Unarmored || ArmorCategories is null || ArmorCategories.Contains(category);

    /// <summary>The rank on <paramref name="track"/> at <paramref name="level"/>: the last change at or below that level.</summary>
    public ProficiencyRank RankAt(ProficiencyTrack track, int level)
    {
        if (!Proficiencies.TryGetValue(track, out var steps))
            return ProficiencyRank.Untrained;
        var rank = ProficiencyRank.Untrained;
        foreach (var (atLevel, stepRank) in steps.OrderBy(s => s.Key))
        {
            if (atLevel > level)
                break;
            rank = stepRank;
        }
        return rank;
    }
}
