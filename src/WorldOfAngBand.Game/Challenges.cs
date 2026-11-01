using System.Globalization;
using System.Text;
using WorldOfAngBand.Engine.Randomness;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Content;

namespace WorldOfAngBand.Game;

/// <summary>A weekly challenge's twist: its id, name and what it does.</summary>
public sealed record Twist(string Id, string Name, string Description);

/// <summary>A seeded challenge run (the spec's section 4.4): the daily dungeon or the weekly challenge, its day or week, and the week's twist.</summary>
public sealed record Challenge(string Kind, string Stamp, string? Twist = null)
{
    public bool IsDaily => Kind == Challenges.Daily;
}

/// <summary>
/// AVABand's daily dungeon and weekly challenge, carried over (the spec's section 4.4). Everyone who plays a day's
/// dungeon gets the same seed and the same hero (a class, ancestry and background drawn from the day); a week's
/// challenge the same for the week (ISO weeks, Monday to Sunday, in UTC), with one of seven twists, turning by three a
/// week so a twist never comes two weeks running. Seeds are 64-bit FNV-1a hashes of "AVAWorldOfAngBand daily
/// yyyy-MM-dd" and "AVAWorldOfAngBand weekly yyyy-Www".
/// </summary>
public static class Challenges
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    public static readonly IReadOnlyList<Twist> Twists =
    [
        new("darkness", "Darkness", "No room in the deep is lit; only your light and the lava show the way."),
        new("glass", "Glass Cannon", "Everyone hits half again as hard: you, and everything that hits you."),
        new("flasks", "Thin Flasks", "You carry two healing flasks, not three."),
        new("swarm", "Swarm", "Half as many monsters again on every level."),
        new("restless", "Restless Foes", "Every monster is a little quicker (+2 speed)."),
        new("gold", "Gold Rush", "Gold is worth twice as much."),
        new("lean", "Lean Times", "Only the Quartermaster and your room at the inn will trade with you."),
    ];

    public static DateOnly Today(DateTime? utcNow = null) => DateOnly.FromDateTime(utcNow ?? DateTime.UtcNow);

    public static string DayStamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The ISO week a day falls in: "2026-W44".</summary>
    public static string WeekStamp(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        return $"{ISOWeek.GetYear(date):0000}-W{ISOWeek.GetWeekOfYear(date):00}";
    }

    public static ulong SeedFor(string kind, string stamp) => Fnv1a($"AVAWorldOfAngBand {kind} {stamp}");

    /// <summary>A week's twist: seven twists, turning by three each week from the week's Monday.</summary>
    public static Twist TwistFor(string week)
    {
        var parts = week.Split("-W");
        var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture), DayOfWeek.Monday));
        var index = monday.DayNumber / 7;
        return Twists[(int)(index * 3L % Twists.Count)];
    }

    /// <summary>Today's dungeon (or a given day's): its seed and its birth.</summary>
    public static (ulong Seed, Birth Birth) DailyFor(DateOnly day, ContentDatabase content, string name)
    {
        var stamp = DayStamp(day);
        var seed = SeedFor(Daily, stamp);
        return (seed, new Birth(HeroFor(seed, content, name), Challenge: new Challenge(Daily, stamp)));
    }

    /// <summary>This week's challenge (or a given day's week): its seed and its birth, with the week's twist.</summary>
    public static (ulong Seed, Birth Birth) WeeklyFor(DateOnly day, ContentDatabase content, string name)
    {
        var stamp = WeekStamp(day);
        var seed = SeedFor(Weekly, stamp);
        return (seed, new Birth(HeroFor(seed, content, name), Challenge: new Challenge(Weekly, stamp, TwistFor(stamp).Id)));
    }

    /// <summary>The challenge's hero: a playable class and its subclass, an ancestry and a background, drawn from the seed.</summary>
    public static CharacterChoices HeroFor(ulong seed, ContentDatabase content, string name)
    {
        var rng = new GameRandom(GameRandom.DeriveSeed(seed, 0x5EED));
        var classes = content.Classes.Keys.Where(c => Powers.IsPlayable(content, c)).Order().ToList();
        var classId = classes[rng.RandInt0(classes.Count)];
        var ancestries = content.Ancestries.Keys.Order().ToList();
        var backgrounds = content.Backgrounds.Keys.Order().ToList();
        var subclasses = content.Feats.Values.Where(f => f.Type == FeatType.Subclass && f.Class == classId).Select(f => f.Id).Order().ToList();
        return CharacterBuilder.Recommend(content, name, ancestries[rng.RandInt0(ancestries.Count)], backgrounds[rng.RandInt0(backgrounds.Count)], classId)
            with { Subclass = subclasses.Count > 0 ? subclasses[rng.RandInt0(subclasses.Count)] : null };
    }

    private static ulong Fnv1a(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
