using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Province;

namespace WorldOfAngBand.Tests;

public class ProvinceStoryTests
{
    private static readonly ProvinceMap Map = ProvinceMap.Shared;

    private static DungeonSession In(string zone, ulong seed)
    {
        var game = new DungeonSession(TestData.Data, seed);
        game.EnterZone(zone, null);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        return game;
    }

    private static List<string> Listen(DungeonSession game)
    {
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        return messages;
    }

    private static void WalkInto(DungeonSession game, Loc at)
    {
        var beside = game.Level.Neighbors(at).Where(p => game.Level.IsPassable(p)).OrderBy(p => p.DistanceTo(at)).First();
        game.Hero.Position = beside;
        game.Perform(new Walk(DirectionExtensions.FromOffset(at.X - beside.X, at.Y - beside.Y)));
    }

    private static Loc Place(string zone, IReadOnlyList<ZonePlace>? places, string id) => ZoneBuilder.LevelAt(places!.First(p => p.Id == id).At);

    [Fact]
    public void The_people_who_stayed_stand_where_ashen_put_them_and_say_their_piece()
    {
        var game = In("ironhold", 131);
        Assert.Contains(game.People, p => p.Name == "Captain Oswin Thale");
        // Hilde, Ironhold's smith, stands at her stall.
        Assert.Contains(game.People, p => p.Name == "Hilde");
        var messages = Listen(game);
        WalkInto(game, Place("ironhold", Map["ironhold"]!.Npcs, "thale"));
        Assert.Contains(messages, m => m.StartsWith("Captain Oswin Thale, Ironhold Guard: \"Ironhold held", StringComparison.Ordinal));
        // Those whose turn in Ashen's story is over are gone.
        var oriel = In("saint_oriels_rest", 132);
        Assert.Single(oriel.People, p => p.Name == "Brother Caddoc");
    }

    [Fact]
    public void What_the_hero_reads_goes_in_the_journal()
    {
        var game = In("gallows_road", 133);
        ReadingEvent? read = null;
        game.Published += e => { if (e is ReadingEvent r) read = r; };
        WalkInto(game, Place("gallows_road", Map["gallows_road"]!.Interactables, "daughter_toy"));
        Assert.Equal("A child's toy", read?.Title);
        var entry = Assert.Single(game.Journal);
        Assert.Equal("Gallows Road", entry.Where);
        Assert.False(string.IsNullOrWhiteSpace(entry.Text));
    }

    [Theory]
    [InlineData("sallows_mire", "sallows_hearth", "mire", "font", "named")]
    [InlineData("bell_tower", "agnete_hymnal", "choir", "library", "sung")]
    [InlineData("barrow_fields", "ediths_grave", "oath", "oath", "released")]
    [InlineData("wyrms_nest", "egg", "nest", "nest", "mourned")]
    public void A_tie_in_read_above_opens_a_third_choice_below(string zone, string thing, string tieIn, string story, string choice)
    {
        var game = In(zone, 134);
        WalkInto(game, Place(zone, Map[zone]!.Interactables, thing));
        Assert.True(game.Flags.ContainsKey(DungeonSession.TieInFlag(tieIn)));
        Assert.Contains(Story.Choices(story, game.Flags, monsterAlive: true), c => c.Value == choice);
        Assert.DoesNotContain(Story.Choices(story, new Dictionary<string, string>(), monsterAlive: true), c => c.Value == choice);
        Assert.Contains(choice == "sung" ? "hymn" : choice == "named" ? "name" : choice == "released" ? "Edith" : "shard",
            Story.WrenSays(game.Flags, 0) + string.Join(" ", Story.Choices(story, game.Flags, true).Select(c => c.Text)));
    }

    [Fact]
    public void Releasing_the_oath_lays_all_nine_down()
    {
        var game = In("barrow_fields", 135);
        WalkInto(game, Place("barrow_fields", Map["barrow_fields"]!.Interactables, "ediths_grave"));
        game.JumpTo(45);
        Assert.True(game.Perform(new ChooseStory("oath", Story.Choices("oath", game.Flags, true).ToList().FindIndex(c => c.Value == "released"))));
        var nine = TestData.Data.Monsters.Where(m => game.Bridge.NameOf(m).Contains("of the Nine", StringComparison.Ordinal)).ToList();
        Assert.Equal(9, nine.Count);
        for (var depth = 70; depth <= 95; depth += 5)
        {
            game.JumpTo(depth);
            Assert.DoesNotContain(game.Foes, f => nine.Contains(f.Monster.Race));
        }
        Assert.Contains("all of them", string.Join(" ", Story.Epilogue("destroy", game.Flags, "Human", "Fighter")));
    }

    [Fact]
    public void A_waystation_rift_once_touched_takes_the_hero_there_from_gallowmere()
    {
        var game = In("ironhold", 136);
        RiftEvent? offered = null;
        game.Published += e => { if (e is RiftEvent r) offered = r; };
        WalkInto(game, Place("ironhold", Map["ironhold"]!.Shrines, "ward_shrine"));
        Assert.Contains("ironhold", game.Rifts);
        Assert.Contains(offered!.Destinations, d => d.Id == ProvinceMap.Gallowmere);
        Assert.True(game.Perform(new StepThroughRift(ProvinceMap.Gallowmere)));
        Assert.Null(game.Zone);
        Assert.Equal(0, game.Depth);
        Assert.True(game.Perform(new StepThroughRift("ironhold")));
        Assert.Equal("ironhold", game.Zone?.Id);
        Assert.False(game.Perform(new StepThroughRift("ember_gate")));
    }

    [Fact]
    public void Ash_storms_blow_over_the_wastes_and_halve_sight()
    {
        var game = In("ash_wastes", 137);
        var seen = new HashSet<bool>();
        for (var i = 0; i < 160; i++)
        {
            game.Perform(new Hold());
            seen.Add(game.Storming);
            if (game.Storming)
                Assert.Equal(TestData.Data.Constants.MaxSight / 2, game.SightRadius);
        }
        Assert.Equal([false, true], seen.Order());
    }

    [Fact]
    public void The_cold_of_the_pass_bites_without_a_ward()
    {
        var game = In("frostbitten_pass", 138);
        var messages = Listen(game);
        for (var i = 0; i < 1200 && !messages.Any(m => m.StartsWith("The cold bites", StringComparison.Ordinal)); i++)
        {
            game.Hero.Character.Heal(1000);
            game.Perform(new Hold());
        }
        Assert.Contains(messages, m => m.StartsWith("The cold bites", StringComparison.Ordinal));
    }
}
