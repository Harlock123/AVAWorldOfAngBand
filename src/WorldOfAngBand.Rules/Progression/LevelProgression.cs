namespace WorldOfAngBand.Rules.Progression;

[Flags]
public enum LevelGain
{
    None = 0,
    AncestryFeat = 1 << 0,
    ClassFeat = 1 << 1,
    SkillFeat = 1 << 2,
    GeneralFeat = 1 << 3,
    SkillIncrease = 1 << 4,
    ClassFeature = 1 << 5,
    AttributeBoosts = 1 << 6,
    Subclass = 1 << 7,
}

/// <summary>The spec's level table: what every class gains at each level, 1 to 20.</summary>
public static class LevelProgression
{
    /// <summary>The first level after <paramref name="level"/> that brings a skill increase; null past 20.</summary>
    public static int? NextSkillIncrease(int level)
    {
        for (var next = Math.Max(1, level + 1); next <= 20; next++)
        {
            if ((GainsAt(next) & LevelGain.SkillIncrease) != 0)
                return next;
        }
        return null;
    }

    public static LevelGain GainsAt(int level)
    {
        if (level is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Levels run from 1 to 20.");

        // Level 1: ancestry feat, background skill feat, class features and the subclass choice.
        if (level == 1)
            return LevelGain.AncestryFeat | LevelGain.SkillFeat | LevelGain.ClassFeature | LevelGain.Subclass;

        var gains = LevelGain.None;
        if (level % 2 == 0)
            gains |= LevelGain.ClassFeat | LevelGain.SkillFeat;
        else
            gains |= LevelGain.SkillIncrease | LevelGain.ClassFeature;
        if (level is 5 or 9 or 13 or 17)
            gains |= LevelGain.AncestryFeat;
        if (level is 3 or 7 or 11 or 15 or 19)
            gains |= LevelGain.GeneralFeat;
        if (level is 5 or 10 or 15 or 20)
            gains |= LevelGain.AttributeBoosts;
        return gains;
    }
}

/// <summary>
/// Pathfinder-style XP: 1,000 XP fills every level. Dying costs 10% of a level (100 XP), but never drops
/// the character below the start of the current level.
/// </summary>
public sealed class Experience
{
    public const int XpPerLevel = 1000;
    public const int MaxLevel = 20;
    public const double DeathPenaltyFraction = 0.10;

    public int Level { get; private set; } = 1;

    /// <summary>XP earned toward the next level, 0 to 999.</summary>
    public int Xp { get; private set; }

    /// <summary>Adds XP and returns how many levels were gained.</summary>
    public int Add(int xp)
    {
        if (xp <= 0 || Level >= MaxLevel)
            return 0;
        Xp += xp;
        var gained = 0;
        while (Xp >= XpPerLevel && Level < MaxLevel)
        {
            Xp -= XpPerLevel;
            Level++;
            gained++;
        }
        if (Level >= MaxLevel)
            Xp = 0;
        return gained;
    }

    internal void Restore(int level, int xp)
    {
        Level = Math.Clamp(level, 1, MaxLevel);
        Xp = Math.Clamp(xp, 0, XpPerLevel - 1);
    }

    public void ApplyDeathPenalty() => Xp = Math.Max(0, Xp - (int)(XpPerLevel * DeathPenaltyFraction));
}
