using System.Text.Json;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App.Services;

/// <summary>
/// The Order's book of the Lantern-sworn: every dead hero's line, kept in graveyard.json in the game's folder of the
/// user's application data. A book that can't be read starts afresh rather than stopping the game.
/// </summary>
public sealed class Graveyard(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Tests point this elsewhere, so they never write in the player's book.</summary>
    public static string DefaultPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVAWorldOfAngBand", "graveyard.json");

    public static Graveyard Default => new(DefaultPath);

    public string FilePath { get; } = path;

    public IReadOnlyList<DeathRecord> Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<DeathRecord>>(File.ReadAllText(FilePath), Options) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Adds a line, written to a new file beside the old one first, so a failure leaves the book as it was.</summary>
    public IReadOnlyList<DeathRecord> Add(DeathRecord record)
    {
        var book = Read().Append(record).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".new";
            File.WriteAllText(temp, JsonSerializer.Serialize(book, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The book stays in memory for this death's screen.
        }
        return book;
    }
}
