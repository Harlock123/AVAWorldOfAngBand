namespace WorldOfAngBand.Game;

/// <summary>A choice at a story level's altar: what the hero can do, and the flag it sets.</summary>
public sealed record StoryChoice(string Text, string Value);

/// <summary>
/// A story level (the spec's section 7.3): at its depth the generator builds its room (data/setting/templates), with
/// its monster where the room's ')' is and its altar where '(' is. At the altar the hero chooses.
/// </summary>
public sealed record StoryLevel(string Id, int Depth, string Room, string? Monster, string Title, string Prompt);

/// <summary>
/// The story: Ashen Reliquary carried on (the spec's section 2). Ten years after the Cinder Throne the Reliquary's floor
/// turns out to be a seal over the Pyre, a shaft cut by the first Order down to where they first burned Ashur. Each
/// character hears one of four rumours of how Ashen Reliquary ended; the rumour decides which Herald's echo waits at
/// depth 98. Five story levels hold a choice each, and the epilogue reads them.
/// </summary>
public static class Story
{
    public const int EchoDepth = 98;
    public const int YsmereDepth = 99;
    public const int CinderDepth = 100;
    public const string Ysmere = "sauron_the_sorcerer";
    public const string Cinder = "morgoth_lord_of_darkness";

    public static readonly IReadOnlyList<StoryLevel> Levels =
    [
        new("font", 15, "story_font", "rot_mother", "The Rot-Mother's Font",
            "Grey water seeps from the Rot-Mother's font into the Pyre's wells, and up them into Gallowmere's."),
        new("library", 33, "story_library", "hollow_choir", "The Drowned Library",
            "On a drowned lectern lie the last chapters of the Library of Ash, the ones the Ashbound never found."),
        new("oath", 45, "story_oath", "unburied_commander", "The Oath Hall",
            "Ser Hadric's oath-stone stands in the hall, and the oath cut in it still holds the Nine Unburied."),
        new("nest", 72, "story_nest", "smaug_the_golden", "The Brood Nest",
            "A warm egg lies in the Cinderwyrm's nest. The dam watches you over it, and does not strike."),
        new("pyre", YsmereDepth, "story_pyre", Ysmere, "The First Pyre",
            "Saint Ysmere tends the first pyre, as she has for nine hundred years, feeding it herself."),
        new("cinder", CinderDepth, "story_cinder", Cinder, "The Cinder of Ashur", ""),
    ];

    public static StoryLevel? At(int depth) => Levels.FirstOrDefault(l => l.Depth == depth);

    /// <summary>The four rumours of how Ashen Reliquary ended, and which Herald's echo each leaves at depth 98.</summary>
    public static readonly IReadOnlyList<(string Ending, string Rumour, string Echo)> Rumours =
    [
        ("destroy", "They say the Ashbound broke the Ember Heart on the Throne and died of it, and that Mother Sallow's relic was never found.", "rot_mother"),
        ("bind", "They say the Ashbound took the god's fire into their own chest, and walks the province still, and that the Choir's candles never all went out.", "hollow_choir"),
        ("claim", "They say the Ashbound sat on the Cinder Throne and burned Vardmoor in their image, and kept Ser Hadric's oath alive to serve them.", "unburied_commander"),
        ("mercy", "They say Malachar took the Heart and walked into the fire with it, and that the Cinderwyrm's dam flew south that night.", "smaug_the_golden"),
    ];

    /// <summary>What the hero can do at a story level's altar, given what they have done so far.</summary>
    public static IReadOnlyList<StoryChoice> Choices(string id, IReadOnlyDictionary<string, string> flags, bool monsterAlive) => id switch
    {
        "font" => [new("Seal the font with the Lantern's wax.", "sealed"), new("Fill a flask from it and quench your blade in it.", "taken")],
        "library" => [new("Read the last chapters.", "read"), new("Leave them to the water.", "left")],
        "oath" when monsterAlive => [new("Strike his name from the stone, and free him.", "freed"), new("Hold him to his oath, and fight him.", "fought")],
        "oath" => [new("Break the stone.", "broken")],
        "nest" when monsterAlive => [new("Let her go: she has her own quarrel with Saint Ysmere.", "spared"), new("Smash the egg.", "smashed")],
        "nest" => [new("Smash the egg.", "smashed")],
        "pyre" when monsterAlive && flags.GetValueOrDefault("library") == "read" =>
            [new("Tell her what the Library says: that the Order lied to her too.", "spared"), new("Fight her.", "fought")],
        "pyre" when monsterAlive => [new("Fight her.", "fought")],
        _ => [],
    };

    /// <summary>What Wren, keeper of the Lantern Archive, says: the next of the story's levels, and what she knows of it.</summary>
    public static string WrenSays(IReadOnlyDictionary<string, string> flags, int maxDepth)
    {
        if (flags.ContainsKey("cinder"))
            return "Wren closes the last ledger. \"It's out. Whatever you choose to do now, you did that.\"";
        var next = Levels.FirstOrDefault(l => !flags.ContainsKey(l.Id) && l.Id != "cinder");
        return next?.Id switch
        {
            "font" => "Wren taps a map of the Lid. \"Something fouls the wells from below, about fifteen levels down. Sallow's work, or what's left of her.\"",
            "library" => "\"The Library of Ash had a drowned wing,\" Wren says. \"Thirty-three levels down, if the Order's surveys are right. Read what's there. My father never could.\"",
            "oath" => "\"Ser Hadric swore the Nine to a war,\" says Wren. \"The oath is cut in a stone forty-five levels down. Oaths can be broken. So can stones.\"",
            "nest" => "Wren unrolls a scorched chart. \"The Cinderwyrm's dam nests seventy-two levels down. She hates the Pyre's keeper more than she hates you. Remember that.\"",
            "pyre" => maxDepth >= 90
                ? "\"Saint Ysmere is at ninety-nine,\" Wren says quietly. \"If you read the Library, you know what to tell her. If not, you'll have to kill a saint.\""
                : "\"Below all of it is the first pyre, and the woman who kept it burning,\" says Wren. \"Go down. I'll be here.\"",
            _ => "\"The Cinder is at the bottom,\" says Wren. \"A hundred levels. Put it out.\"",
        };
    }

    /// <summary>The epilogue after the Cinder of Ashur goes out: the rumour, the choices, the hero.</summary>
    public static IReadOnlyList<string> Epilogue(string rumour, IReadOnlyDictionary<string, string> flags, string ancestry, string characterClass)
    {
        var lines = new List<string>
        {
            "The Cinder of Ashur goes out, and for the first time in nine hundred years the Pyre is dark and cold.",
            rumour switch
            {
                "destroy" => "In Gallowmere they put a second name beside the Ashbound's on the gallows-stone.",
                "bind" => "Somewhere in Vardmoor the Lantern feels the god's last coal go out, and sleeps for the first time in ten years.",
                "claim" => "On the Cinder Throne the New Prophet feels the fire beneath it die, and is only a ruler of ashes after all.",
                _ => "A wooden bird turns up on the Lantern Archive's sill. Wren keeps it on her desk.",
            },
        };
        lines.Add(flags.GetValueOrDefault("font") == "sealed"
            ? "The wells of Gallowmere run clear again by spring."
            : "The wells of Gallowmere stay grey for a generation, and the healers grow rich.");
        lines.Add(flags.GetValueOrDefault("library") == "read"
            ? "Wren copies out the Library's last chapters, and the rekindled Order reads, at last, what the first Order did."
            : "The Library's last chapters rot in the water, and the Order never learns what it was.");
        lines.Add(flags.GetValueOrDefault("oath") switch
        {
            "freed" => "Ser Hadric's name is cut from the oath-stone, and the Nine Unburied lie down at last.",
            "fought" or "broken" => "The Oath Hall is quiet. Nobody goes there.",
            _ => "Somewhere below, an oath still holds nine dead knights to a forgotten war.",
        });
        lines.Add(flags.GetValueOrDefault("nest") == "spared"
            ? "The Cinderwyrm's dam was seen once more, over the Ember Peaks, flying north."
            : "No drakes are seen over the Ember Peaks again.");
        lines.Add(flags.GetValueOrDefault("pyre") == "spared"
            ? "Saint Ysmere walks up out of the Pyre, blinking, and asks the date."
            : "Saint Ysmere's name is added to the Order's book, and struck through.");
        lines.Add($"The {ancestry.ToLowerInvariant()} {characterClass.ToLowerInvariant()} who did it climbs back up to Gallowmere, and is never quite warm again.");
        return lines;
    }
}
