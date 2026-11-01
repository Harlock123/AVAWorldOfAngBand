using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.Tests;

public class StoryTests
{
    /// <summary>A level-20 hero at a depth, with only the story's monsters left on it (so the tests are about the story).</summary>
    private static DungeonSession At(int depth, ulong seed = 61)
    {
        var game = new DungeonSession(TestData.Data, seed);
        while (game.Hero.Level < 20)
            game.Hero.Character.LevelUp();
        game.JumpTo(depth);
        Clear(game);
        return game;
    }

    private static void Clear(DungeonSession game)
    {
        var keep = Story.Levels.Select(l => l.Monster).Concat(Story.Rumours.Select(r => r.Echo)).ToHashSet();
        foreach (var f in game.Foes.Where(f => !keep.Contains(f.Monster.Race.Id)).ToList())
            game.Banish(f);
    }

    private static Loc Altar(DungeonSession game) => game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Id == "quest_altar");

    /// <summary>Puts the hero on the altar (as walking onto it would) and returns what it offers.</summary>
    private static StoryChoiceEvent StandAtAltar(DungeonSession game)
    {
        StoryChoiceEvent? offered = null;
        game.Published += e => { if (e is StoryChoiceEvent s) offered = s; };
        var altar = Altar(game);
        var beside = game.Level.Neighbors(altar).First(p => game.Level.IsPassable(p) && game.FoeAt(p) is null);
        game.Hero.Position = beside;
        game.Perform(new Walk(DirectionExtensions.FromOffset(altar.X - beside.X, altar.Y - beside.Y)));
        return offered ?? throw new InvalidOperationException("The altar offered nothing.");
    }

    [Theory]
    [InlineData(15, "rot_mother")]
    [InlineData(33, "hollow_choir")]
    [InlineData(45, "unburied_commander")]
    [InlineData(72, "smaug_the_golden")]
    [InlineData(99, "sauron_the_sorcerer")]
    [InlineData(100, "morgoth_lord_of_darkness")]
    public void Each_story_level_has_its_room_and_its_monster(int depth, string monster)
    {
        var game = At(depth);
        Assert.Contains(game.Foes, f => f.Monster.Race.Id == monster);
        if (depth != Story.CinderDepth)
            Assert.Equal("quest_altar", game.Level.FeatureAt(Altar(game)).Id);
    }

    [Fact]
    public void Saint_ysmeres_level_has_no_way_down_until_she_is_gone_and_her_death_opens_one()
    {
        var game = At(99);
        Assert.Empty(game.Level.FindFeature(TerrainFlags.DownStair));
        var ysmere = game.Foes.First(f => f.Monster.Race.Id == Story.Ysmere);
        game.Fight.Hurt(ysmere, 1_000_000);
        Assert.Single(game.Level.FindFeature(TerrainFlags.DownStair));
        Assert.Equal("slain", game.Flags["pyre"]);
    }

    [Fact]
    public void Reading_the_library_lets_the_hero_talk_saint_ysmere_down()
    {
        var game = At(33);
        var library = StandAtAltar(game);
        Assert.Equal("library", library.StoryId);
        Assert.True(game.Perform(new ChooseStory("library", library.Choices.ToList().FindIndex(c => c.Value == "read"))));
        Assert.Equal("read", game.Flags["library"]);
        game.JumpTo(99);
        Clear(game);
        var pyre = StandAtAltar(game);
        Assert.Contains(pyre.Choices, c => c.Value == "spared");
        game.Perform(new ChooseStory("pyre", pyre.Choices.ToList().FindIndex(c => c.Value == "spared")));
        Assert.DoesNotContain(game.Foes, f => f.Monster.Race.Id == Story.Ysmere);
        Assert.NotEmpty(game.Level.FindFeature(TerrainFlags.DownStair));
    }

    [Fact]
    public void Without_the_library_saint_ysmere_must_be_fought()
    {
        var game = At(99);
        var pyre = StandAtAltar(game);
        Assert.Equal(["fought"], pyre.Choices.Select(c => c.Value));
    }

    [Fact]
    public void Sealing_the_font_gives_poison_resistance_and_taking_its_water_a_venom_blade()
    {
        var sealedGame = At(15);
        sealedGame.Perform(new ChooseStory("font", StandAtAltar(sealedGame).Choices.ToList().FindIndex(c => c.Value == "sealed")));
        Assert.True(sealedGame.Hero.Character.Defenses.Resistances.ContainsKey(Rules.Combat.DamageType.Poison));

        var taken = At(15, 62);
        taken.Perform(new ChooseStory("font", StandAtAltar(taken).Choices.ToList().FindIndex(c => c.Value == "taken")));
        Assert.Contains(taken.Hero.Pack.Weapon!.Brands, b => b.Element == "pois");
    }

    [Fact]
    public void Freeing_the_commander_ends_his_fight_and_letting_the_dam_go_sends_her_away()
    {
        var game = At(45);
        var oath = StandAtAltar(game);
        game.Perform(new ChooseStory("oath", oath.Choices.ToList().FindIndex(c => c.Value == "freed")));
        Assert.DoesNotContain(game.Foes, f => f.Monster.Race.Id == "unburied_commander");
        Assert.True(game.Hero.Gear.Protects("hold_life"));

        game.JumpTo(72);
        Clear(game);
        var nest = StandAtAltar(game);
        game.Perform(new ChooseStory("nest", nest.Choices.ToList().FindIndex(c => c.Value == "spared")));
        Assert.DoesNotContain(game.Foes, f => f.Monster.Race.Id == "smaug_the_golden");
    }

    [Fact]
    public void Putting_out_the_cinder_wins_with_an_epilogue_and_a_line_in_the_book()
    {
        var game = At(100);
        var won = new List<HeroWonEvent>();
        game.Published += e => { if (e is HeroWonEvent w) won.Add(w); };
        game.Fight.Hurt(game.Foes.First(f => f.Monster.Race.Id == Story.Cinder), 10_000_000);
        Assert.True(game.Won);
        Assert.Single(won);
        Assert.Contains("The Cinder of Ashur goes out", game.Epilogue[0]);
        var record = game.Retire();
        Assert.True(record.Won);
        Assert.Contains("who put out the Cinder of Ashur", record.Epitaph);
    }

    [Fact]
    public void The_rumour_puts_its_heralds_echo_at_depth_ninety_eight()
    {
        foreach (var seed in Enumerable.Range(70, 4).Select(s => (ulong)s))
        {
            var game = At(98, seed);
            var echo = Story.Rumours.First(r => r.Ending == game.Rumour).Echo;
            Assert.Contains(game.Foes, f => f.Monster.Race.Id == echo);
            Assert.Contains(Story.Rumours, r => r.Rumour == game.Opening);
        }
    }

    [Fact]
    public void Past_level_twenty_the_hero_rises_through_five_mythic_tiers()
    {
        var game = new DungeonSession(TestData.Data, 63);
        while (game.Hero.Level < 20)
            game.Hero.Character.LevelUp();
        var hp = game.Hero.MaxHitPoints;
        var attack = game.Hero.Character.AttackModifier().Total;
        // The deepest common monster is worth something to a level-20 hero (Pathfinder's experience by level).
        var deep = TestData.Data.Monsters.Where(m => !m.IsUnique && !m.Has(MonsterFlags.Questor)).MaxBy(m => m.Depth)!;
        Assert.True(CreatureBridgeLevel(deep) >= 18);
        game.JumpTo(100);
        Clear(game);
        for (var i = 0; i < 400 && game.Hero.MythicTier < Hero.MaxMythicTier; i++)
        {
            var spot = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p) && p != game.Hero.Position);
            game.Spawn(deep, spot, asleep: true);
            game.Fight.Hurt(game.FoeAt(spot)!, 10_000_000);
        }
        Assert.Equal(Hero.MaxMythicTier, game.Hero.MythicTier);
        Assert.Equal(hp + 50, game.Hero.MaxHitPoints);
        Assert.Equal(attack + 3, game.Hero.Character.AttackModifier().Total);
    }

    [Fact]
    public void Nothing_sends_the_hero_down_past_saint_ysmere()
    {
        var game = At(Story.YsmereDepth);
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        game.Hero.DescentIn = 1;
        game.Perform(new Hold());
        Assert.Equal(Story.YsmereDepth, game.Depth);
        Assert.Contains(messages, m => m.Contains("holds"));
        game.TeleportLevel();
        game.Perform(new Hold());
        Assert.Equal(Story.YsmereDepth - 1, game.Depth);
        // Deep Descent from above stops at her level, not five below.
        game.JumpTo(97);
        game.Hero.DescentIn = 1;
        game.Perform(new Hold());
        Assert.Equal(Story.YsmereDepth, game.Depth);
    }

    [Fact]
    public void Saint_ysmere_keeps_her_vigil_alone_and_the_shaft_lands_the_hero_near_her()
    {
        var game = new DungeonSession(TestData.Data, 65);
        game.JumpTo(Story.YsmereDepth);
        var ysmere = Assert.Single(game.Foes, f => f.Monster.Race.Id == Story.Ysmere);
        Assert.DoesNotContain(game.Foes, f => f != ysmere && f.Monster.Race.IsUnique && f.Monster.Position.ChebyshevTo(ysmere.Monster.Position) <= 2);
        Assert.InRange(game.Hero.Position.ChebyshevTo(ysmere.Monster.Position), 13, 22);
    }

    [Fact]
    public void A_keeper_left_behind_is_there_when_the_hero_comes_back()
    {
        var game = At(Story.YsmereDepth);
        game.JumpTo(Story.YsmereDepth - 1);
        game.JumpTo(Story.YsmereDepth);
        Assert.Contains(game.Foes, f => f.Monster.Race.Id == Story.Ysmere);
    }

    [Fact]
    public void The_shapes_a_shapechanger_takes_never_turn_up_by_themselves()
    {
        var spawner = new Engine.Monsters.MonsterSpawner(TestData.Data);
        var rng = new Engine.Randomness.GameRandom(66);
        var shapes = TestData.Data.Monsters.SelectMany(m => m.Shapes).ToHashSet();
        Assert.NotEmpty(shapes);
        for (var i = 0; i < 2000; i++)
            Assert.DoesNotContain(spawner.PickRace(rng, 99, new HashSet<string>(), r => r.IsUnique)?.Id ?? "", shapes);
    }

    private static int CreatureBridgeLevel(MonsterRaceDef race) => Game.Bridge.CreatureBridge.LevelForDepth(race.Depth);

    [Fact]
    public void Wren_says_where_the_story_goes_next()
    {
        var game = new DungeonSession(TestData.Data, 64);
        Assert.Contains("fifteen levels down", Story.WrenSays(game.Flags, 0));
    }

    [Fact]
    public void Every_kind_of_level_angband_makes_can_be_made_and_vaults_turn_up_on_the_way_down()
    {
        var generator = new Engine.Generation.DungeonGenerator(TestData.Data);
        foreach (var profile in TestData.Data.Profiles.Where(p => p.Id != "town"))
        {
            var level = generator.Generate(new Engine.Generation.LevelRequest(40, 99, ProfileId: profile.Id)).Level;
            Assert.Equal(profile.Id, level.ProfileId);
        }
        var game = new DungeonSession(TestData.Data, 65);
        var vaults = 0;
        for (var depth = 1; depth <= 98; depth += 3)
        {
            game.JumpTo(depth);
            vaults += game.Level.Vaults.Count;
        }
        Assert.True(vaults > 10);
    }
}
