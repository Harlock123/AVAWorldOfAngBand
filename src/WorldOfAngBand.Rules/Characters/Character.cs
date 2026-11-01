using WorldOfAngBand.Rules.Abilities;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Content;
using WorldOfAngBand.Rules.Items;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Characters;

public enum DeathOutcome
{
    Alive,

    /// <summary>Hit 0 HP, but Second Breath held them at 1 HP.</summary>
    SavedBySecondBreath,

    Died,
}

/// <summary>
/// A player character: ancestry, background and class plus attributes, level, gear and conditions.
/// Every derived statistic comes back as a <see cref="ModifierTotal"/>, so the UI can show the formula
/// behind each number (AC 24 = 10 + DEX 3 + Expert 9 + item 2).
/// </summary>
public sealed class Character : IDefender
{
    public const int MaxLevel = 20;
    public const double BaseDetectionTiles = 4.0;
    public const double DetectionTilesPerPerception = 0.25;

    public static readonly WeaponDefinition Fist = new("fist", "Fist", 4, DamageType.Bludgeoning, 1.6,
        Traits: new HashSet<WeaponTrait> { WeaponTrait.Agile, WeaponTrait.Finesse });

    private readonly List<(StatisticKind Statistic, Modifier Modifier)> _extraModifiers = [];

    internal Character(string name, AncestryDefinition ancestry, BackgroundDefinition background,
        ClassDefinition characterClass, AttributeKind keyAttribute, AttributeSet attributes, HeritageDefinition? heritage = null)
    {
        Name = name;
        Heritage = heritage;
        Ancestry = ancestry;
        Background = background;
        Class = characterClass;
        KeyAttribute = keyAttribute;
        Attributes = attributes;
        var taintRate = ancestry.Id == "ashborn" ? 0.75 : 1.0;
        Taint = new TaintMeter(TaintMeter.RateFor(taintRate, attributes[AttributeKind.Constitution]));
        _baseTaintRate = Taint.RateMultiplier;
        if (heritage is { Will: > 0 })
            AddModifier(StatisticKind.Will, new Modifier(heritage.Name, heritage.Will));
        RefreshDefenses();
        HitPoints = MaxHitPoints;
    }

    public string Name { get; }
    public AncestryDefinition Ancestry { get; }
    public HeritageDefinition? Heritage { get; }
    public BackgroundDefinition Background { get; }
    public ClassDefinition Class { get; }
    public AttributeKind KeyAttribute { get; }
    public AttributeSet Attributes { get; }
    public int Level { get; private set; } = 1;

    public Weapon Weapon { get; set; } = new(Fist);
    public Armor Armor { get; set; } = Armor.None;

    /// <summary>What the equipped items add on top of the rules: affixes and implicit bonuses (see <see cref="ApplyGear"/>).</summary>
    public GearBonuses Gear { get; private set; } = GearBonuses.None;

    /// <summary>
    /// Replaces the gear bonuses: max HP, resistances and untyped save bonuses take effect at once. Hit points keep
    /// their damage taken when the maximum changes.
    /// </summary>
    public void ApplyGear(GearBonuses gear, IReadOnlyList<(string Source, IReadOnlyList<FeatEffect> Effects)>? worn = null)
    {
        var missing = MaxHitPoints - HitPoints;
        Gear = gear;
        RemoveModifiers(GearSource);
        foreach (var (save, value) in gear.Saves)
            AddModifier(save, new Modifier(GearSource, value));
        if (worn is not null || _wornEffects.Count > 0)
        {
            _wornEffects = worn ?? [];
            RebuildEffects();
        }
        RefreshDefenses();
        if (!IsDead)
            HitPoints = Math.Clamp(MaxHitPoints - missing, 1, MaxHitPoints);
    }

    public const string GearSource = "Gear";

    /// <summary>
    /// Resistances from gear, heritage and ancestry together. They add up, as untyped bonuses from different sources
    /// in this game do; the ancestry's share grows with level (Ashborn: fire resistance of half the level).
    /// </summary>
    private void RefreshDefenses()
    {
        Defenses.Resistances.Clear();
        var total = new Dictionary<DamageType, int>();
        foreach (var (type, value) in Gear.Resistances)
            total[type] = total.GetValueOrDefault(type) + value;
        foreach (var (type, value) in Heritage?.Resistances ?? new Dictionary<DamageType, int>())
            total[type] = total.GetValueOrDefault(type) + value;
        foreach (var (type, value) in _featResistances)
            total[type] = total.GetValueOrDefault(type) + value;
        if (Ancestry.FireResistancePerTwoLevels > 0)
            total[DamageType.Fire] = total.GetValueOrDefault(DamageType.Fire) + Math.Max(1, Level / 2) * Ancestry.FireResistancePerTwoLevels;
        foreach (var (type, value) in total)
            Defenses.Resist(type, value);
    }

    public ConditionSet Conditions { get; } = new();

    /// <summary>Bleed, burning, poison on the hero (the Fens' bog, poisoned blades), ticked by the world.</summary>
    public PersistentDamageSet Persistent { get; } = new();
    public DamageProfile Defenses { get; } = new();
    public SecondBreath SecondBreath { get; } = new();
    public HealingPotion Potion { get; } = new();
    public TaintMeter Taint { get; }

    public int HitPoints { get; private set; }
    public bool IsDead { get; private set; }

    /// <summary>Gear affixes, feats and buffs. They go through the normal stacking rule with everything else.</summary>
    public void AddModifier(StatisticKind statistic, Modifier modifier) => _extraModifiers.Add((statistic, modifier));

    public void RemoveModifiers(string source) => _extraModifiers.RemoveAll(m => m.Modifier.Source == source);

    // ----- Levels ----------------------------------------------------------------

    /// <summary>
    /// Gains a level: hit points and proficiencies rise at once. What the level lets the player pick (feats, a skill
    /// increase, attribute boosts) waits in <see cref="PendingLevels"/> until <see cref="Choose"/>; boosts may also be
    /// passed here directly (tests and older saves).
    /// </summary>
    public void LevelUp(IReadOnlyCollection<AttributeKind>? boosts = null)
    {
        if (Level >= MaxLevel)
            throw new RulesException($"Level {MaxLevel} is the cap.");
        var next = Level + 1;
        var needsBoosts = AttributeSet.BoostLevels.Contains(next);
        if (!needsBoosts && boosts is not null)
            throw new RulesException($"Level {next} does not grant attribute boosts.");

        var before = MaxHitPoints;
        if (boosts is not null)
        {
            Attributes.ApplyLevelUpBoosts(next, boosts);
            _levelUpBoosts[next] = boosts.ToArray();
        }
        Level = next;
        RefreshDefenses();
        HitPoints += MaxHitPoints - before;
    }

    private readonly Dictionary<int, AttributeKind[]> _levelUpBoosts = [];
    private readonly Dictionary<int, LevelChoice> _levelChoices = [];

    /// <summary>The picks made at each level (feats, skill increase, boosts), so a save can replay them.</summary>
    public IReadOnlyDictionary<int, LevelChoice> LevelChoices => _levelChoices;

    /// <summary>Whether a level grants anything to pick (every level from 2 does).</summary>
    public static bool LevelHasChoices(int level) => level >= 2 && (Progression.LevelProgression.GainsAt(level)
        & (Progression.LevelGain.ClassFeat | Progression.LevelGain.SkillFeat | Progression.LevelGain.GeneralFeat
           | Progression.LevelGain.AncestryFeat | Progression.LevelGain.SkillIncrease | Progression.LevelGain.AttributeBoosts)) != 0;

    /// <summary>The levels reached whose picks haven't been made yet, lowest first.</summary>
    public IReadOnlyList<int> PendingLevels =>
        Enumerable.Range(2, Math.Max(0, Level - 1)).Where(l => LevelHasChoices(l) && !_levelChoices.ContainsKey(l)).ToList();

    /// <summary>
    /// Makes a level's picks. Everything the level grants must be chosen and nothing else: a class feat at 2 and 4,
    /// a skill feat at 2 and 4, a general feat at 3, an ancestry feat at 5, a skill increase at 3 and 5, and four
    /// attribute boosts at 5 (unless they were given when levelling). Each feat must be one this character may take.
    /// </summary>
    public void Choose(int level, LevelChoice choice)
    {
        if (level < 2 || level > Level)
            throw new RulesException($"Level {level} hasn't been reached.");
        if (_levelChoices.ContainsKey(level))
            throw new RulesException($"Level {level}'s picks are already made.");
        var gains = Progression.LevelProgression.GainsAt(level);
        bool Has(Progression.LevelGain gain) => (gains & gain) != 0;
        var feats = new List<FeatDefinition>();
        void Want(Progression.LevelGain gain, string? pick, FeatType type, string what)
        {
            if (!Has(gain))
            {
                if (pick is not null)
                    throw new RulesException($"Level {level} doesn't grant {what}.");
                return;
            }
            if (pick is null)
                throw new RulesException($"Level {level} grants {what}: choose one.");
            var feat = Feat(pick) ?? throw new RulesException($"Unknown feat '{pick}'.");
            if (feat.Type != type || !CanTake(feat, level))
                throw new RulesException($"{feat.Name} can't be taken as {what} at level {level}.");
            if (feats.Contains(feat))
                throw new RulesException($"{feat.Name} is picked twice.");
            feats.Add(feat);
        }
        Want(Progression.LevelGain.ClassFeat, choice.ClassFeat, FeatType.Class, "a class feat");
        Want(Progression.LevelGain.SkillFeat, choice.SkillFeat, FeatType.Skill, "a skill feat");
        Want(Progression.LevelGain.GeneralFeat, choice.GeneralFeat, FeatType.General, "a general feat");
        Want(Progression.LevelGain.AncestryFeat, choice.AncestryFeat, FeatType.Ancestry, "an ancestry feat");

        if (Has(Progression.LevelGain.SkillIncrease))
        {
            if (choice.SkillIncrease is not { } skill)
                throw new RulesException($"Level {level} grants a skill increase: choose a skill.");
            if (!CanIncrease(skill, level))
                throw new RulesException($"{skill} can't be raised at level {level}.");
        }
        else if (choice.SkillIncrease is not null)
        {
            throw new RulesException($"Level {level} doesn't grant a skill increase.");
        }

        var needsBoosts = Has(Progression.LevelGain.AttributeBoosts) && !_levelUpBoosts.ContainsKey(level);
        if (needsBoosts && choice.Boosts is null)
            throw new RulesException($"Level {level} grants {AttributeSet.BoostsPerLevelUp} attribute boosts.");
        if (!needsBoosts && choice.Boosts is not null)
            throw new RulesException($"Level {level} has no attribute boosts to give.");

        var before = MaxHitPoints;
        if (choice.Boosts is { } boosts)
        {
            Attributes.ApplyLevelUpBoosts(level, boosts);
            _levelUpBoosts[level] = boosts.ToArray();
        }
        if (choice.SkillIncrease is { } raised)
            _skillRanks[raised] = SkillRank(raised) + 1;
        foreach (var feat in feats)
            Grant(feat);
        _levelChoices[level] = choice;
        RefreshDefenses();
        HitPoints += MaxHitPoints - before;
    }

    /// <summary>Whether this character may take a feat at <paramref name="level"/>: its level, ancestry, class, subclass, skill training, not taken yet.</summary>
    public bool CanTake(FeatDefinition feat, int level) =>
        feat.Level <= level && !_feats.Contains(feat)
        && (feat.Ancestry is null || feat.Ancestry == Ancestry.Id)
        && (feat.Class is null || feat.Class == Class.Id)
        && (feat.Subclass is null || feat.Subclass == Subclass?.Id)
        && (feat.Dedication is null || feat.Dedication != Class.Id && Dedication is null)
        && (feat.Skill is null || SkillRank(feat.Skill) >= ProficiencyRank.Trained);

    // ----- Subclass --------------------------------------------------------------

    /// <summary>The subclass chosen at level 1 (its feat), or null for a class without them or a save from before them.</summary>
    public FeatDefinition? Subclass => _feats.FirstOrDefault(f => f.Type == FeatType.Subclass);

    /// <summary>The multiclass dedication taken (its feat), or null: one per character.</summary>
    public FeatDefinition? Dedication => _feats.FirstOrDefault(f => f.Dedication is not null);

    /// <summary>The class the dedication opens, or null.</summary>
    public ClassDefinition? DedicationClass =>
        Dedication?.Dedication is { } id && Content?.Classes.GetValueOrDefault(id) is { } c ? c : null;

    /// <summary>The subclasses this character's class offers, in the file's order.</summary>
    public IReadOnlyList<FeatDefinition> SubclassOptions =>
        Content is null ? [] : Content.Feats.Values.Where(f => f.Type == FeatType.Subclass && f.Class == Class.Id).ToList();

    /// <summary>Whether the subclass is still to choose: the class has subclasses and none is taken (a save from before them).</summary>
    public bool NeedsSubclass => Subclass is null && SubclassOptions.Count > 0;

    /// <summary>
    /// Takes a subclass after creation (a save from before subclasses). It counts as a level-1 pick: a respec keeps it,
    /// and the creation choices record it so the save rebuilds it.
    /// </summary>
    public void ChooseSubclass(string id)
    {
        if (Subclass is { } taken)
            throw new RulesException($"{Name} is already a {taken.Name}.");
        var feat = SubclassOptions.FirstOrDefault(f => f.Id == id)
            ?? throw new RulesException($"'{id}' isn't a {Class.Name} subclass.");
        var before = MaxHitPoints;
        Grant(feat);
        _creationFeats?.Add(feat);
        if (Choices is { } choices)
            Choices = choices with { Subclass = id };
        HitPoints += MaxHitPoints - before;
    }

    /// <summary>Whether a skill increase at <paramref name="level"/> may raise this skill a rank (expert from 3, master from 7).</summary>
    public bool CanIncrease(string skill, int level) =>
        Skills.Attribute.ContainsKey(skill) && SkillRank(skill) < ProficiencyRank.Legendary
        && Proficiency.MinimumLevelForSkillRank(SkillRank(skill) + 1) <= level;

    /// <summary>The feats this character may take at a level, of a type (for the level-up screen).</summary>
    public IReadOnlyList<FeatDefinition> FeatOptions(FeatType type, int level) =>
        Content is null ? [] : Content.Feats.Values.Where(f => f.Type == type && CanTake(f, level)).ToList();

    private FeatDefinition? Feat(string id) => Content?.Feats.GetValueOrDefault(id);

    /// <summary>The content this character was built from (feats, skills); set by <see cref="CharacterBuilder"/>.</summary>
    internal ContentDatabase? Content { get; set; }

    /// <summary>Back to full health (after level-1 feats raise the maximum).</summary>
    internal void RestoreHitPointsToMax() => HitPoints = MaxHitPoints;

    // ----- Respec ----------------------------------------------------------------

    private AttributeSet? _creationAttributes;
    private Dictionary<string, ProficiencyRank>? _creationSkills;
    private List<FeatDefinition>? _creationFeats;

    /// <summary>Remembers the level-1 character (CharacterBuilder, at the end of creation) so a respec can go back to it.</summary>
    internal void MarkCreated()
    {
        _creationAttributes = Attributes.Clone();
        _creationSkills = new Dictionary<string, ProficiencyRank>(_skillRanks, StringComparer.OrdinalIgnoreCase);
        _creationFeats = [.. _feats];
    }

    /// <summary>
    /// Forgets every pick made since level 1 (feats, skill increases, attribute boosts) so each level can be chosen
    /// again; level, XP and gear stay. Damage taken stays taken.
    /// </summary>
    public void Respec()
    {
        if (_creationAttributes is null || _creationSkills is null || _creationFeats is null)
            throw new RulesException("Only characters made by CharacterBuilder can respec.");
        var missing = MaxHitPoints - HitPoints;
        Attributes.CopyFrom(_creationAttributes);
        _skillRanks.Clear();
        foreach (var (skill, rank) in _creationSkills)
            _skillRanks[skill] = rank;
        _feats.Clear();
        _feats.AddRange(_creationFeats);
        RebuildEffects();
        _levelChoices.Clear();
        _levelUpBoosts.Clear();
        RefreshDefenses();
        if (!IsDead)
            HitPoints = Math.Clamp(MaxHitPoints - missing, 1, MaxHitPoints);
    }

    // ----- Skills ----------------------------------------------------------------

    private readonly Dictionary<string, ProficiencyRank> _skillRanks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The skills trained or better (Lore skills aside: the background's Lore is always trained).</summary>
    public IReadOnlyDictionary<string, ProficiencyRank> SkillRanks => _skillRanks;

    public ProficiencyRank SkillRank(string skill) =>
        Skills.IsLore(skill)
            ? string.Equals(skill, Background.Lore, StringComparison.OrdinalIgnoreCase) ? ProficiencyRank.Trained : ProficiencyRank.Untrained
            : _skillRanks.GetValueOrDefault(skill);

    internal void Train(string skill)
    {
        if (SkillRank(skill) < ProficiencyRank.Trained)
            _skillRanks[skill] = ProficiencyRank.Trained;
    }

    // ----- Feats -----------------------------------------------------------------

    private readonly List<FeatDefinition> _feats = [];
    private readonly List<(string Skill, Modifier Modifier)> _skillBonuses = [];
    private readonly Dictionary<DamageType, int> _featResistances = [];
    private readonly Dictionary<string, List<FeatEffect>> _abilityMods = [];
    private readonly List<string> _grantedAbilities = [];

    public IReadOnlyList<FeatDefinition> Feats => _feats;

    /// <summary>The abilities feats have added to the bar, in the order they were gained.</summary>
    public IReadOnlyList<string> GrantedAbilities => _grantedAbilities;

    public int FeatHitPointsPerLevel { get; private set; }
    public int FeatSpeedFeet { get; private set; }
    public double RecoveryBonus { get; private set; }
    public int CritDamageBonus { get; private set; }
    public int FuryPerHitBonus { get; private set; }
    public double ResourceRegenBonus { get; private set; }
    public double ReachBonus { get; private set; }
    public double SpellRangeBonus { get; private set; }
    public bool IgnoresArmorSpeed { get; private set; }

    /// <summary>How much more Shield Block takes off a blow with a shield (the Bulwark).</summary>
    public int BlockHardnessBonus { get; private set; }

    /// <summary>Whether a Shield Block is followed by a strike back at the attacker (the Bulwark).</summary>
    public bool BlockCounter { get; private set; }

    /// <summary>Sneak attack's precision d6s against an off-guard target: the class's, plus feats'. None for most classes.</summary>
    public int SneakAttackDice => Class.SneakAttackDiceAt(Level) is var dice and > 0 ? dice + SneakDiceBonus : 0;

    public int SneakDiceBonus { get; private set; }

    /// <summary>A Strike out of hiding that hits is a critical hit (the Cutthroat).</summary>
    public bool AmbushCrit { get; private set; }

    /// <summary>No slower while hidden (the Cutthroat).</summary>
    public bool SwiftSneak { get; private set; }

    /// <summary>Enemies next to a Tumble's path are left off-guard (the Trickster).</summary>
    public bool TumbleFeint { get; private set; }

    /// <summary>How many more enemies every arrow passes through (the Marksman).</summary>
    public int ArrowPierce { get; private set; }

    /// <summary>The resource a critical hit with a bow gives back (the Marksman).</summary>
    public double CritResourceBonus { get; private set; }

    /// <summary>Extra tiles of bow range (Far Shot).</summary>
    public double WeaponRangeBonus { get; private set; }

    public int PreyDiceBonus { get; private set; }

    /// <summary>The animal companion's creature id (the Beastmaster's wolf), or null.</summary>
    public string? Companion { get; private set; }

    /// <summary>A pet's creature id (a boon's), beside any companion.</summary>
    public string? Pet { get; private set; }

    /// <summary>How far feats have grown the companion (Pack Hunter).</summary>
    public int CompanionGrowth { get; private set; }

    private int UndeadBaneBonus { get; set; }

    /// <summary>More skeletons the Necromancer can hold at once (the Bonelord's, Legion).</summary>
    public int MinionBonus { get; private set; }

    /// <summary>How far feats have made the Necromancer's skeletons sturdier.</summary>
    public int MinionGrowth { get; private set; }

    /// <summary>More Grave Charges held, and more from each corpse (feats).</summary>
    public int GraveChargeBonus { get; private set; }
    public int ChargesPerCorpseBonus { get; private set; }

    /// <summary>
    /// The Lanternbearer's undead bane: vitality damage added to every Strike that hits an undead monster (doubled on
    /// a critical hit), 1, one more at levels 9 and 17. None without the feature.
    /// </summary>
    public int UndeadBane => UndeadBaneBonus > 0 ? UndeadBaneBonus + (Level >= 9 ? 1 : 0) + (Level >= 17 ? 1 : 0) : 0;

    /// <summary>How much more Lay on Hands heals, as a share (the Lanternbearer's half again).</summary>
    public double HealShare { get; private set; }

    /// <summary>Tiles of light the hero gives off (the Lanternbearer's lantern), on top of what they carry.</summary>
    public double LightBonus { get; private set; }

    /// <summary>
    /// The Oathbreaker's Taint: their hits fill their own Taint meter, and their Strikes deal Taint damage by how full
    /// it is, healing them as much.
    /// </summary>
    public bool TaintFuel { get; private set; }

    /// <summary>The Gunslinger's Singular Expertise: damage with a gun, 2, 3 from level 7, 4 from level 15.</summary>
    public int SingularExpertise => 2 + (Level >= 7 ? 1 : 0) + (Level >= 15 ? 1 : 0);

    /// <summary>The Gunslinger's: reloads quicker by this share, rounds more in every gun, a round back on a critical hit.</summary>
    public double ReloadSpeedBonus { get; private set; }
    public int CapacityBonus { get; private set; }
    public bool CritReload { get; private set; }

    /// <summary>The Sniper's: more damage with a gun at a target <c>GameWorld.DistantTiles</c> off or more.</summary>
    public int DistantDamage { get; private set; }

    /// <summary>More special rounds from each casting at the forge (Munitions Crafter).</summary>
    public int RoundsBonus { get; private set; }

    /// <summary>
    /// The Paladin's Oath of Justice: their champion's reaction is always ready, a Strike back at a monster that hits
    /// them or an ally beside them (at most once every <c>GameWorld.RetributionSeconds</c>).
    /// </summary>
    public bool Retribution { get; private set; }

    /// <summary>Hunt Prey's precision d8s against the hunted monster: the class's, plus feats'. None for most classes.</summary>
    public int PreyDice => Class.PreyDiceAt(Level) + PreyDiceBonus;

    public int ExtraMissiles(string abilityId) => _abilityMods.GetValueOrDefault(abilityId)?.Sum(m => m.ExtraMissiles) ?? 0;

    /// <summary>
    /// An ability as this character's feats have changed it: more targets or missiles, a lower cost, a wider burst, a
    /// longer ward (see <see cref="FeatEffect.ModifiesAbility"/>). Unchanged when no feat touches it.
    /// </summary>
    public AbilityDefinition Modify(AbilityDefinition ability)
    {
        if (!_abilityMods.TryGetValue(ability.Id, out var mods))
            return ability;
        foreach (var m in mods)
        {
            ability = ability with
            {
                Missiles = ability.Missiles + m.ExtraMissiles,
                Targets = ability.Targets + m.Targets,
                Cost = Math.Max(0, ability.Cost + m.Cost),
                Knockback = ability.Knockback + m.Knockback,
                Chain = ability.Chain + m.Chain,
                Splash = ability.Splash + m.Splash,
                Radius = ability.Radius + m.Radius,
                OnHitSeconds = ability.OnHitSeconds + m.OnHitSeconds,
                WardSeconds = ability.WardSeconds + m.WardSeconds,
                Pierce = ability.Pierce + m.Pierce,
                BuffSeconds = ability.BuffSeconds + m.BuffSeconds,
                ExtraDice = ability.ExtraDice + m.ExtraDice,
                Cooldown = Math.Max(0, ability.Cooldown + m.Cooldown),
                PersistentDice = m.PersistentDice != 0 && ability.PersistentDice is { } text
                    ? (Rules.Dice.Parse(text) is var d ? (d with { Count = d.Count + m.PersistentDice }).ToString() : text)
                    : ability.PersistentDice,
            };
        }
        return ability;
    }

    /// <summary>Applies a feat's effects (see <see cref="FeatEffect"/>).</summary>
    internal void Grant(FeatDefinition feat)
    {
        if (_feats.Contains(feat))
            return;
        _feats.Add(feat);
        ApplyEffects(feat.Name, feat.Effects);
        RefreshDefenses();
    }

    private readonly double _baseTaintRate;
    private readonly HashSet<string> _effectSources = [];
    private IReadOnlyList<(string Source, IReadOnlyList<FeatEffect> Effects)> _wornEffects = [];

    /// <summary>
    /// The effects worn items bring (a Unique's trait, a Set's bonuses), as feat effects with their sources; they last
    /// while the items are worn (see <see cref="ApplyGear"/>).
    /// </summary>
    public IReadOnlyList<(string Source, IReadOnlyList<FeatEffect> Effects)> WornEffects => _wornEffects;

    /// <summary>
    /// Works every effect out afresh: everything feats and worn items gave is taken away, then every feat taken and
    /// every worn effect is applied again (gear changed, or a respec).
    /// </summary>
    private void RebuildEffects()
    {
        foreach (var source in _effectSources)
            RemoveModifiers(source);
        _effectSources.Clear();
        _skillBonuses.Clear();
        _featResistances.Clear();
        _abilityMods.Clear();
        _grantedAbilities.Clear();
        FeatHitPointsPerLevel = FeatSpeedFeet = CritDamageBonus = FuryPerHitBonus = BlockHardnessBonus = SneakDiceBonus = ArrowPierce = PreyDiceBonus = CompanionGrowth = 0;
        UndeadBaneBonus = MinionBonus = MinionGrowth = GraveChargeBonus = ChargesPerCorpseBonus = CapacityBonus = DistantDamage = RoundsBonus = 0;
        Companion = null;
        Pet = null;
        RecoveryBonus = ResourceRegenBonus = ReachBonus = SpellRangeBonus = CritResourceBonus = WeaponRangeBonus = HealShare = LightBonus = ReloadSpeedBonus = 0;
        IgnoresArmorSpeed = BlockCounter = AmbushCrit = SwiftSneak = TumbleFeint = TaintFuel = Retribution = CritReload = false;
        Taint.ResetRate(_baseTaintRate);
        foreach (var feat in _feats)
            ApplyEffects(feat.Name, feat.Effects);
        foreach (var (source, effects) in _wornEffects)
            ApplyEffects(source, effects);
        RefreshDefenses();
    }

    private void ApplyEffects(string source, IEnumerable<FeatEffect> effects)
    {
        _effectSources.Add(source);
        foreach (var e in effects)
        {
            if (e.Statistic is { } stat && Enum.TryParse<StatisticKind>(stat, out var kind))
                AddModifier(kind, new Modifier(source, e.Value));
            if (e.Skill is { } skill)
                _skillBonuses.Add((skill, new Modifier(source, e.Value, BonusType.Circumstance)));
            if (e.Resist is { } type)
                _featResistances[type] = _featResistances.GetValueOrDefault(type) + e.Value;
            if (e.Ability is { } ability)
            {
                if (e.ModifiesAbility)
                {
                    if (!_abilityMods.TryGetValue(ability, out var mods))
                        _abilityMods[ability] = mods = [];
                    mods.Add(e);
                }
                else if (!_grantedAbilities.Contains(ability))
                {
                    _grantedAbilities.Add(ability);
                }
            }
            BlockHardnessBonus += e.BlockHardness;
            BlockCounter |= e.BlockCounter;
            SneakDiceBonus += e.SneakDice;
            AmbushCrit |= e.AmbushCrit;
            SwiftSneak |= e.SwiftSneak;
            TumbleFeint |= e.TumbleFeint;
            if (e.Ability is null)
                ArrowPierce += e.Pierce;
            CritResourceBonus += e.CritResource;
            WeaponRangeBonus += e.WeaponRange;
            PreyDiceBonus += e.PreyDice;
            Companion = e.Companion ?? Companion;
            Pet = e.Pet ?? Pet;
            CompanionGrowth += e.CompanionGrowth;
            UndeadBaneBonus += e.UndeadBane;
            HealShare += e.HealShare;
            LightBonus += e.Light;
            TaintFuel |= e.TaintFuel;
            Retribution |= e.Retribution;
            ReloadSpeedBonus += e.ReloadSpeed;
            CapacityBonus += e.Capacity;
            CritReload |= e.CritReload;
            DistantDamage += e.DistantDamage;
            RoundsBonus += e.RoundsBonus;
            MinionBonus += e.Minions;
            MinionGrowth += e.MinionGrowth;
            GraveChargeBonus += e.GraveCharges;
            ChargesPerCorpseBonus += e.ChargesPerCorpse;
            FeatHitPointsPerLevel += e.HitPointsPerLevel;
            FeatSpeedFeet += e.SpeedFeet;
            RecoveryBonus += e.Recovery;
            CritDamageBonus += e.CritDamage;
            FuryPerHitBonus += e.FuryPerHit;
            ResourceRegenBonus += e.ResourceRegen;
            ReachBonus += e.Reach;
            SpellRangeBonus += e.SpellRange;
            IgnoresArmorSpeed |= e.IgnoreArmorSpeed;
            if (e.TaintRate > 0)
                Taint.SlowBy(e.TaintRate);
        }
    }

    /// <summary>The boosts chosen at each boost level, so a save can replay the character's growth exactly.</summary>
    public IReadOnlyDictionary<int, AttributeKind[]> LevelUpBoosts => _levelUpBoosts;

    /// <summary>The character creation choices this character was built from (set by <see cref="CharacterBuilder"/>).</summary>
    public CharacterChoices? Choices { get; internal set; }

    public ProficiencyRank Rank(ProficiencyTrack track) => Class.RankAt(track, Level);

    // ----- Hit points ------------------------------------------------------------

    /// <summary>Ancestry HP + (class HP + CON) x level, less drained N x level.</summary>
    public int MaxHitPoints =>
        Math.Max(1, Ancestry.HitPoints + (Class.HitPointsPerLevel + Attributes[AttributeKind.Constitution]) * Level
            - Conditions.MaxHitPointLoss(Level) + Gear.MaxHitPoints + ((Heritage?.HitPointsPerLevel ?? 0) + FeatHitPointsPerLevel) * Level);

    /// <summary>Takes damage. At 0 HP, Second Breath (if ready) holds the character at 1 HP; otherwise they die.</summary>
    public DeathOutcome TakeDamage(int amount)
    {
        if (IsDead || amount <= 0)
            return IsDead ? DeathOutcome.Died : DeathOutcome.Alive;
        HitPoints = Math.Max(0, HitPoints - amount);
        if (HitPoints > 0)
            return DeathOutcome.Alive;
        if (SecondBreath.TrySpend())
        {
            HitPoints = 1;
            return DeathOutcome.SavedBySecondBreath;
        }
        IsDead = true;
        return DeathOutcome.Died;
    }

    /// <summary>Puts hit points back to an exact value (after an equip preview), within the current maximum.</summary>
    internal void RestoreHitPoints(int hitPoints)
    {
        if (!IsDead)
            HitPoints = Math.Clamp(hitPoints, 1, MaxHitPoints);
    }

    public void Heal(int amount)
    {
        if (!IsDead && amount > 0)
            HitPoints = Math.Min(MaxHitPoints, HitPoints + amount);
    }

    /// <summary>Respawning at a shrine: full HP, potion charges refilled, Taint cleansed, drained removed.</summary>
    public void RestAtShrine()
    {
        IsDead = false;
        Conditions.Remove(ConditionKind.Drained);
        Persistent.Clear();
        Taint.Cleanse();
        Potion.Refill();
        HitPoints = MaxHitPoints;
    }

    // ----- Derived statistics ----------------------------------------------------

    /// <summary>10 + DEX (up to the armour's cap) + armour proficiency + armour item bonus.</summary>
    public ModifierTotal ArmorClass()
    {
        var dex = Math.Min(Attributes[AttributeKind.Dexterity], Armor.Definition.DexCap);
        return Total(StatisticKind.ArmorClass, AttributeKind.Dexterity,
            new Modifier("Base", 10),
            Attr(AttributeKind.Dexterity, dex),
            Class.IsTrainedIn(Armor.Definition.Category) ? Prof(ProficiencyTrack.Armor) : new Modifier("Untrained in armour", 0),
            new Modifier(Armor.Definition.Name, Armor.ItemAcBonus, BonusType.Item));
    }

    /// <summary>The attribute a weapon attacks with: DEX for ranged, the better of STR and DEX for finesse, otherwise STR.</summary>
    public AttributeKind AttackAttribute(Weapon weapon)
    {
        if (weapon.Definition.IsRanged)
            return AttributeKind.Dexterity;
        if (weapon.Definition.Has(WeaponTrait.Finesse) && Attributes[AttributeKind.Dexterity] > Attributes[AttributeKind.Strength])
            return AttributeKind.Dexterity;
        return AttributeKind.Strength;
    }

    /// <summary>STR or DEX + weapon proficiency + potency (item) + status and circumstance modifiers.</summary>
    public ModifierTotal AttackModifier()
    {
        var attribute = AttackAttribute(Weapon);
        return Total(StatisticKind.Attack, attribute,
            Attr(attribute, Attributes[attribute]),
            Prof(ProficiencyTrack.Attack),
            new Modifier("Potency rune", Weapon.Potency, BonusType.Item));
    }

    /// <summary>A thrown bomb's attack: DEX + the class's attack proficiency (alchemical bombs are simple weapons).</summary>
    public ModifierTotal ThrowAttack() => Total(StatisticKind.Attack, AttributeKind.Dexterity,
        Attr(AttributeKind.Dexterity, Attributes[AttributeKind.Dexterity]),
        Prof(ProficiencyTrack.Attack));

    /// <summary>
    /// The flat damage added to the weapon's dice: STR for melee and thrown weapons, nothing for other ranged weapons.
    /// A rogue (<see cref="ClassDefinition.FinesseDamage"/>) adds DEX instead with a finesse weapon, if it is higher; a
    /// gunslinger (<see cref="ClassDefinition.GunDamage"/>) adds its Singular Expertise with a gun instead: 1, 2 from level 7,
    /// 3 from level 15.
    /// </summary>
    public ModifierTotal DamageBonus()
    {
        if (Class.GunDamage && Weapon.Definition.Firearm)
            return Total(StatisticKind.Damage, null, new Modifier("Singular Expertise", SingularExpertise),
                new Modifier(GearSource, Gear.WeaponDamage));
        var addsStrength = !Weapon.Definition.IsRanged || Weapon.Definition.Has(WeaponTrait.Thrown);
        var attribute = addsStrength && Class.FinesseDamage && Weapon.Definition.Has(WeaponTrait.Finesse)
            && Attributes[AttributeKind.Dexterity] > Attributes[AttributeKind.Strength] ? AttributeKind.Dexterity : AttributeKind.Strength;
        return Total(StatisticKind.Damage, addsStrength ? attribute : null,
            Attr(attribute, addsStrength ? Attributes[attribute] : 0),
            new Modifier(GearSource, Gear.WeaponDamage));
    }

    /// <summary>Fortitude (CON), Reflex (DEX) or Will (WIS) + proficiency + resilient rune (item).</summary>
    public ModifierTotal Save(StatisticKind save)
    {
        var (attribute, track) = save switch
        {
            StatisticKind.Fortitude => (AttributeKind.Constitution, ProficiencyTrack.Fortitude),
            StatisticKind.Reflex => (AttributeKind.Dexterity, ProficiencyTrack.Reflex),
            StatisticKind.Will => (AttributeKind.Wisdom, ProficiencyTrack.Will),
            _ => throw new ArgumentOutOfRangeException(nameof(save), save, "Not a save."),
        };
        var taint = Taint.SavePenalty;
        return Total(save, attribute,
            Attr(attribute, Attributes[attribute]),
            Prof(track),
            new Modifier("Resilient rune", Armor.Resilient, BonusType.Item),
            taint ?? default);
    }

    public ModifierTotal Perception() => Total(StatisticKind.Perception, AttributeKind.Wisdom,
        Attr(AttributeKind.Wisdom, Attributes[AttributeKind.Wisdom]),
        Prof(ProficiencyTrack.Perception));

    /// <summary>
    /// A skill: its attribute + proficiency rank (<see cref="SkillRank"/>) + the armour's check penalty for Strength
    /// and Dexterity skills + feats' circumstance bonuses (the best one counts).
    /// </summary>
    public ModifierTotal Skill(string skill) => SkillAt(skill, SkillRank(skill));

    /// <summary>A skill as it would be at <paramref name="rank"/> (the level-up screen's preview of an increase).</summary>
    public ModifierTotal SkillAt(string skill, ProficiencyRank rank)
    {
        var attribute = Skills.AttributeFor(skill);
        var penalty = attribute is AttributeKind.Strength or AttributeKind.Dexterity
            ? Armor.CheckPenalty(Attributes[AttributeKind.Strength]) : 0;
        var bonuses = _skillBonuses
            .Where(b => string.Equals(b.Skill, skill, StringComparison.OrdinalIgnoreCase)
                || (b.Skill == "Lore" && Skills.IsLore(skill))
                || (b.Skill == "Untrained" && rank == ProficiencyRank.Untrained))
            .Select(b => b.Modifier);
        return Total(StatisticKind.Skill, attribute,
            [Attr(attribute, Attributes[attribute]),
             new Modifier(rank.ToString(), Proficiency.Bonus(rank, Level)),
             new Modifier("Armour check penalty", penalty),
             .. bonuses]);
    }

    /// <summary>Traps, hidden doors and stealthy enemies are spotted within 4 tiles + 0.25 per point of Perception.</summary>
    public double DetectionRangeTiles => BaseDetectionTiles + DetectionTilesPerPerception * Perception().Total;

    /// <summary>10 + key attribute + class DC proficiency.</summary>
    public ModifierTotal ClassDc() => Total(StatisticKind.ClassDc, KeyAttribute,
        new Modifier("Base", 10),
        Attr(KeyAttribute, Attributes[KeyAttribute]),
        Prof(ProficiencyTrack.ClassDc));

    public ModifierTotal SpellAttack() => Total(StatisticKind.SpellAttack, KeyAttribute,
        Attr(KeyAttribute, Attributes[KeyAttribute]),
        Prof(ProficiencyTrack.Spell));

    /// <summary>10 + key attribute + spell proficiency.</summary>
    public ModifierTotal SpellDc() => Total(StatisticKind.SpellDc, KeyAttribute,
        new Modifier("Base", 10),
        Attr(KeyAttribute, Attributes[KeyAttribute]),
        Prof(ProficiencyTrack.Spell));

    /// <summary>Ancestry speed less the armour's speed penalty, in feet.</summary>
    public int SpeedFeet => Ancestry.SpeedFeet + (Heritage?.SpeedFeet ?? 0) + FeatSpeedFeet
        - (IgnoresArmorSpeed ? 0 : Armor.SpeedPenaltyFeet(Attributes[AttributeKind.Strength]));

    public double MoveSpeedTilesPerSecond(double moveSpeedBonus = 0) =>
        Conditions.CanMove ? ActionTiming.MoveSpeedTilesPerSecond(SpeedFeet, moveSpeedBonus + Gear.MoveSpeed) : 0;

    // ----- Helpers ---------------------------------------------------------------

    private static Modifier Attr(AttributeKind kind, int value) => new(kind.Abbreviation(), value);

    private Modifier Prof(ProficiencyTrack track)
    {
        var rank = Rank(track);
        return new Modifier(rank.ToString(), Proficiency.Bonus(rank, Level));
    }

    private ModifierTotal Total(StatisticKind statistic, AttributeKind? attribute, params Modifier[] core) =>
        ModifierStack.Combine(core
            .Concat(_extraModifiers.Where(m => m.Statistic == statistic).Select(m => m.Modifier))
            .Concat(Conditions.PenaltiesFor(statistic, attribute)));
}
