using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Progression;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>
/// A level's picks made for the player (the level-up window starts from them, and the bots take them): the first feat
/// of each kind the level grants that the hero may take, a skill increase for a skill they already know best, and
/// boosts to the key attribute, Constitution, Dexterity and Wisdom.
/// </summary>
public static class LevelPicks
{
    public static LevelChoice Suggest(Character character, int level)
    {
        var gains = LevelProgression.GainsAt(level);
        bool Has(LevelGain gain) => (gains & gain) != 0;
        string? First(LevelGain gain, FeatType type) => Has(gain) ? character.FeatOptions(type, level).FirstOrDefault()?.Id : null;
        var skill = Has(LevelGain.SkillIncrease)
            ? Skills.Attribute.Keys.Where(s => character.CanIncrease(s, level))
                .OrderByDescending(character.SkillRank).ThenBy(s => Skills.Attribute[s] == character.KeyAttribute ? 0 : 1)
                .FirstOrDefault()
            : null;
        var boosts = Has(LevelGain.AttributeBoosts) && !character.LevelUpBoosts.ContainsKey(level)
            ? new[] { character.KeyAttribute, AttributeKind.Constitution, AttributeKind.Dexterity, AttributeKind.Wisdom }
                .Concat(Enum.GetValues<AttributeKind>()).Distinct().Take(AttributeSet.BoostsPerLevelUp).ToArray()
            : null;
        return new LevelChoice(boosts, First(LevelGain.ClassFeat, FeatType.Class), First(LevelGain.SkillFeat, FeatType.Skill),
            First(LevelGain.GeneralFeat, FeatType.General), First(LevelGain.AncestryFeat, FeatType.Ancestry), skill);
    }

    /// <summary>Makes every pending level's suggested picks (the bots, and heroes made ready for a depth).</summary>
    public static void TakeAll(DungeonSession game)
    {
        foreach (var level in game.Hero.Character.PendingLevels)
            game.Perform(new ChooseLevel(level, Suggest(game.Hero.Character, level)));
    }
}
