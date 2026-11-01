using System.Text.Json;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App.Services;

/// <summary>One try at a challenge: which, the try's number, the hero, how deep they got and how it ended.</summary>
public sealed record ChallengeTry(string Kind, string Stamp, int Attempt, string Name, string Class, int Level, int MaxDepth,
    int Points, string Fate, bool Won, DateTimeOffset At, bool Modded = false)
{
    /// <summary>A line to share: "Daily 2026-11-01, try 2: Vessa, level 9 Priest, 1450 ft, killed by a cave troll (1350 points)."</summary>
    public string ShareLine =>
        $"{(Kind == Challenges.Daily ? "Daily" : "Weekly")} {Stamp}, try {Attempt}: {Name}, level {Level} {Class}, "
        + $"{MaxDepth * DungeonSession.FeetPerLevel} ft, {Fate} ({Points} points){(Modded ? " [modded]" : "")}.";

    /// <summary>AVABand's score, more or less: a hundred a level of depth, fifty a character level, ten thousand for the win.</summary>
    public static int PointsFor(int maxDepth, int level, bool won) => maxDepth * 100 + level * 50 + (won ? 10_000 : 0);
}

/// <summary>
/// The daily and weekly boards (AVABand's TryBoard): every try at a challenge, kept in challenges.json beside the
/// Order's book. A board that can't be read starts afresh.
/// </summary>
public sealed class ChallengeBoard(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(Graveyard.DefaultPath)!, "challenges.json");

    public string FilePath { get; } = path;

    public IReadOnlyList<ChallengeTry> Read()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<ChallengeTry>>(File.ReadAllText(FilePath), Options) ?? [] : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>A challenge's tries, best first: deepest, then most points.</summary>
    public IReadOnlyList<ChallengeTry> For(string kind, string stamp) =>
        Read().Where(t => t.Kind == kind && t.Stamp == stamp).OrderByDescending(t => t.MaxDepth).ThenByDescending(t => t.Points).ToList();

    /// <summary>Writes down how a challenge run ended (death or the win); its try's number is one more than that challenge's tries so far.</summary>
    public ChallengeTry Record(DungeonSession game)
    {
        var challenge = game.Birth.Challenge ?? throw new InvalidOperationException("Not a challenge run.");
        var c = game.Hero.Character;
        var all = Read().ToList();
        var fate = game.Won ? "put out the Cinder" : game.Death is { } death ? $"killed by {death.Killer}" : "gave up";
        var entry = new ChallengeTry(challenge.Kind, challenge.Stamp, all.Count(t => t.Kind == challenge.Kind && t.Stamp == challenge.Stamp) + 1,
            c.Name, c.Class.Name, c.Level, game.Hero.MaxDepth, ChallengeTry.PointsFor(game.Hero.MaxDepth, c.Level, game.Won), fate, game.Won,
            DateTimeOffset.UtcNow, game.Data.ModDirectories.Any(d => d != Data.DataLoader.SettingDirectory && d != Data.DataLoader.ProvinceDirectory));
        all.Add(entry);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return entry;
    }
}
