using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Province;

namespace WorldOfAngBand.Audio;

/// <summary>What the player is looking at, besides the game itself: each has its own music (the spec's section 8.1).</summary>
public enum SoundScreen
{
    Game,

    /// <summary>The birth window, or the book of the dead after a death.</summary>
    Title,

    /// <summary>The epilogue, after the Cinder goes out.</summary>
    Epilogue,
}

/// <summary>
/// Where the hero is, in sound (the spec's section 8.1): the music's mood and the ambience loop for a place. Gallowmere
/// and the waystations play the town's music; the province's open zones Ashen's exploring music, its dark ones (and
/// the shaft to depth 30) the dungeon's; the shaft below, the deep's. Ambience follows a zone's tileset, or the
/// dungeon's profile and depth as in AVABand. A zone may name its own in the province notes.
/// </summary>
public static class Soundscape
{
    /// <summary>A zone this dark or darker is indoors: halls, crypts and caves.</summary>
    public const double IndoorDarkness = 0.85;

    /// <summary>The deepest level of the shallow dungeon's music and ambience, and of the deep's ambience.</summary>
    public const int ShallowTo = 30, DeepTo = 60;

    public static bool Indoors(ZoneDef zone) => zone.Darkness >= IndoorDarkness;

    /// <summary>The mood for a screen, or for the place the hero is in.</summary>
    public static string Mood(DungeonSession session, SoundScreen screen = SoundScreen.Game) => screen switch
    {
        SoundScreen.Title => "title",
        SoundScreen.Epilogue => "epilogue",
        _ => PlaceMood(session),
    };

    public static string PlaceMood(DungeonSession session)
    {
        if (session.Zone is { } zone)
            return session.Province.Notes.Sound?.GetValueOrDefault(zone.Id)?.Music
                   ?? (zone.Town ? "town" : Indoors(zone) ? "dungeon" : "explore");
        return session.Depth switch
        {
            0 => "town",
            <= ShallowTo => "dungeon",
            _ => "deep",
        };
    }

    /// <summary>Moods to try, in order, when a pack has none for the one wanted.</summary>
    public static IEnumerable<string> Fallbacks(string mood) => mood switch
    {
        "epilogue" => ["epilogue", "title"],
        "deep" => ["deep", "dungeon"],
        "explore" => ["explore", "town"],
        _ => [mood],
    };

    /// <summary>The ambience loops for where the hero is, best first (none on the title screens).</summary>
    public static IReadOnlyList<string> Ambience(DungeonSession session, SoundScreen screen = SoundScreen.Game)
    {
        if (screen != SoundScreen.Game)
            return [];
        if (session.Zone is { } zone)
        {
            if (session.Province.Notes.Sound?.GetValueOrDefault(zone.Id)?.Ambience is { } own)
                return [own];
            return zone.Tileset switch
            {
                _ when Indoors(zone) && zone.Tileset == "tileset_cinder" => ["lair", "dungeon-deep"],
                _ when Indoors(zone) => ["dungeon-shallow"],
                "tileset_ironhold" => ["fortress", "town-day"],
                "tileset_oriel" => ["labyrinth", "town-day"],
                "tileset_cinder" => ["gauntlet", "labyrinth"],
                _ => ["town-day"],
            };
        }
        return Dungeon(session.Depth, session.Level.ProfileId);
    }

    /// <summary>
    /// AVABand's: a cavern, labyrinth or fortress has its own loop (moria and lair fall back to the cavern's, a gauntlet
    /// to the labyrinth's); otherwise the town, or the dungeon by depth (shallow, deep, the abyss), each falling back to
    /// the one above it.
    /// </summary>
    public static IReadOnlyList<string> Dungeon(int depth, string profile)
    {
        if (depth == 0)
            return ["town-day"];
        string[] bands = depth > DeepTo ? ["dungeon-abyss", "dungeon-deep", "dungeon-shallow"]
            : depth > ShallowTo ? ["dungeon-deep", "dungeon-shallow"]
            : ["dungeon-shallow"];
        return profile switch
        {
            "hard_centre" => ["fortress", .. bands],
            "cavern" or "labyrinth" => [profile, .. bands],
            "moria" or "lair" => [profile, "cavern", .. bands],
            "gauntlet" => ["gauntlet", "labyrinth", .. bands],
            _ => bands,
        };
    }
}
