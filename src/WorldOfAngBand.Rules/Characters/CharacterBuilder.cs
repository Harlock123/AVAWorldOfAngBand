using WorldOfAngBand.Rules.Content;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Characters;

/// <summary>Everything the player picks on the character-creation screen.</summary>
public sealed record CharacterChoices(
    string Name,
    string AncestryId,
    string BackgroundId,
    string ClassId,
    AttributeKind KeyAttribute,
    IReadOnlyList<AttributeKind> AncestryFreeBoosts,
    AttributeKind BackgroundBoost,
    AttributeKind BackgroundFreeBoost,
    IReadOnlyList<AttributeKind> FreeBoosts,
    string? HeritageId = null,
    IReadOnlyList<string>? Skills = null,
    string? AncestryFeat = null,
    string? Subclass = null);

/// <summary>
/// Builds a level-1 character in Pathfinder order: ancestry, background, class, then four free boosts.
/// Each group of boosts must go to different attributes, and no attribute may start above +4.
/// </summary>
public static class CharacterBuilder
{
    public const int FreeBoostCount = 4;

    public static Character Create(ContentDatabase content, CharacterChoices choices)
    {
        var ancestry = content.Ancestry(choices.AncestryId);
        var background = content.Background(choices.BackgroundId);
        var characterClass = content.Class(choices.ClassId);
        var attributes = new AttributeSet();

        // Ancestry: fixed boosts plus its free ones, all to different attributes, then any flaw.
        if (choices.AncestryFreeBoosts.Count != ancestry.FreeBoosts)
            throw new RulesException($"{ancestry.Name} takes {ancestry.FreeBoosts} free boost(s), not {choices.AncestryFreeBoosts.Count}.");
        ApplyGroup(attributes, [.. ancestry.Boosts, .. choices.AncestryFreeBoosts], "Ancestry");
        foreach (var flaw in ancestry.Flaws)
            attributes.Flaw(flaw);

        // Background: one of its two listed boosts plus one free boost to a different attribute.
        if (!background.BoostChoices.Contains(choices.BackgroundBoost))
            throw new RulesException($"{background.Name} boosts {string.Join(" or ", background.BoostChoices)}, not {choices.BackgroundBoost}.");
        ApplyGroup(attributes, [choices.BackgroundBoost, choices.BackgroundFreeBoost], "Background");

        // Class: its key attribute.
        if (!characterClass.KeyAttributes.Contains(choices.KeyAttribute))
            throw new RulesException($"{characterClass.Name}'s key attribute is {string.Join(" or ", characterClass.KeyAttributes)}, not {choices.KeyAttribute}.");
        attributes.Boost(choices.KeyAttribute);

        if (choices.FreeBoosts.Count != FreeBoostCount)
            throw new RulesException($"Choose exactly {FreeBoostCount} free boosts, not {choices.FreeBoosts.Count}.");
        ApplyGroup(attributes, choices.FreeBoosts, "Free");

        foreach (var kind in Enum.GetValues<AttributeKind>())
        {
            if (attributes[kind] > AttributeSet.CreationCap)
                throw new RulesException($"{kind} would start at {attributes[kind]:+0}; the cap at level 1 is +{AttributeSet.CreationCap}.");
        }

        HeritageDefinition? heritage = null;
        if (choices.HeritageId is { } heritageId)
            heritage = ancestry.Heritages?.FirstOrDefault(h => h.Id == heritageId)
                ?? throw new RulesException($"{ancestry.Name} has no heritage '{heritageId}'.");
        if (string.IsNullOrWhiteSpace(choices.Name))
            throw new RulesException("Your character needs a name.");

        var character = new Character(choices.Name.Trim(), ancestry, background, characterClass, choices.KeyAttribute, attributes, heritage)
        {
            Choices = choices,
            Content = content,
        };

        // Skills: the background's and the class's are trained, plus the class's picks + INT (older saves have none
        // recorded and get the recommended ones).
        character.Train(background.Skill);
        foreach (var skill in characterClass.TrainedSkills ?? [])
            character.Train(skill);
        var picks = SkillPicks(characterClass, attributes);
        var chosen = choices.Skills ?? RecommendSkills(character, picks);
        if (chosen.Count != Math.Min(picks, UntrainedSkills(character).Count))
            throw new RulesException($"Choose {picks} skill(s) to be trained in, not {chosen.Count}.");
        foreach (var skill in chosen)
        {
            if (!Skills.Attribute.ContainsKey(skill))
                throw new RulesException($"Unknown skill '{skill}'.");
            if (character.SkillRank(skill) >= ProficiencyRank.Trained)
                throw new RulesException($"You are already trained in {skill}.");
            character.Train(skill);
        }

        // Feats: the background's skill feat, and an ancestry feat (older saves get the first one).
        if (content.Feats.Values.FirstOrDefault(f => f.Name == background.SkillFeat) is { } backgroundFeat)
            character.Grant(backgroundFeat);
        var ancestryFeats = AncestryFeats(content, ancestry.Id);
        if (choices.AncestryFeat is { } featId)
        {
            var feat = ancestryFeats.FirstOrDefault(f => f.Id == featId)
                ?? throw new RulesException($"'{featId}' isn't a level-1 {ancestry.Name} feat.");
            character.Grant(feat);
        }
        else if (ancestryFeats.Count > 0)
        {
            character.Grant(ancestryFeats[0]);
        }
        // The subclass (spec: level 1). Saves from before subclasses have none and choose it on the level-up screen.
        if (choices.Subclass is { } subclassId)
        {
            var subclass = character.SubclassOptions.FirstOrDefault(f => f.Id == subclassId)
                ?? throw new RulesException($"'{subclassId}' isn't a {characterClass.Name} subclass.");
            character.Grant(subclass);
        }
        character.RestoreHitPointsToMax();
        character.MarkCreated();
        return character;
    }

    /// <summary>How many extra skills a class lets a new character train: its own number plus Intelligence (if positive).</summary>
    public static int SkillPicks(ClassDefinition characterClass, AttributeSet attributes) =>
        characterClass.SkillPicks + Math.Max(0, attributes[AttributeKind.Intelligence]);

    /// <summary>The skills a character isn't trained in yet, alphabetically.</summary>
    public static IReadOnlyList<string> UntrainedSkills(Character character) =>
        Skills.Attribute.Keys.Where(s => character.SkillRank(s) < ProficiencyRank.Trained).Order().ToList();

    /// <summary>The level-1 ancestry feats open to an ancestry, in the file's order.</summary>
    public static IReadOnlyList<FeatDefinition> AncestryFeats(ContentDatabase content, string ancestryId) =>
        content.Feats.Values.Where(f => f.Type == FeatType.Ancestry && f.Level == 1 && f.Ancestry == ancestryId).ToList();

    /// <summary>Sensible skills to train: those using the character's best attributes first, alphabetically within.</summary>
    private static IReadOnlyList<string> RecommendSkills(Character character, int picks) =>
        UntrainedSkills(character).OrderByDescending(s => character.Attributes[Skills.AttributeFor(s)]).ThenBy(s => s).Take(picks).ToList();

    /// <summary>
    /// A legal, sensible set of boosts for an ancestry, background and class: the key attribute first, then CON,
    /// DEX and WIS, then the rest. The character creation screen's "Recommended" button uses this.
    /// </summary>
    public static CharacterChoices Recommend(ContentDatabase content, string name, string ancestryId, string backgroundId,
        string classId, AttributeKind? keyAttribute = null, string? heritageId = null)
    {
        var ancestry = content.Ancestry(ancestryId);
        var background = content.Background(backgroundId);
        var characterClass = content.Class(classId);
        var key = keyAttribute is { } k && characterClass.KeyAttributes.Contains(k) ? k : characterClass.KeyAttributes[0];
        var priority = new[] { key, AttributeKind.Constitution, AttributeKind.Dexterity, AttributeKind.Wisdom }
            .Concat(Enum.GetValues<AttributeKind>()).Distinct().ToList();

        var ancestryFree = priority.Where(a => !ancestry.Boosts.Contains(a)).Take(ancestry.FreeBoosts).ToList();
        var backgroundBoost = background.BoostChoices.OrderBy(priority.IndexOf).First();
        var backgroundFree = priority.First(a => a != backgroundBoost);
        var free = priority.Take(FreeBoostCount).ToList();

        // Keep every attribute at +4 or less: if the key attribute would pass the cap, its free boost moves on.
        var choices = new CharacterChoices(name, ancestryId, backgroundId, classId, key, ancestryFree, backgroundBoost,
            backgroundFree, free, heritageId ?? ancestry.Heritages?.FirstOrDefault()?.Id);
        return choices;
    }

    private static void ApplyGroup(AttributeSet attributes, IReadOnlyList<AttributeKind> boosts, string group)
    {
        if (boosts.Distinct().Count() != boosts.Count)
            throw new RulesException($"{group} boosts must go to different attributes.");
        foreach (var kind in boosts)
            attributes.Boost(kind);
    }
}
