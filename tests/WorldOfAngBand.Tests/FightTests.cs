using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Bots;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Creatures;

namespace WorldOfAngBand.Tests;

public class FightTests
{
    /// <summary>A game on its first level, with every square known.</summary>
    private static DungeonSession FirstLevel(ulong seed)
    {
        var game = new DungeonSession(TestData.Data, seed);
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        foreach (var p in game.Level.AllLocs())
            game.Known.Remember(game.Level, p);
        return game;
    }

    /// <summary>Puts a monster of <paramref name="raceId"/> beside the hero, awake, on an open square.</summary>
    private static Foe Beside(DungeonSession game, string raceId, out Direction toward)
    {
        foreach (var d in DirectionExtensions.Compass)
        {
            var p = game.Hero.Position.Step(d);
            if (!game.Level.IsEmptyFloor(p))
                continue;
            game.Spawn(TestData.Data.Monster(raceId)!, p, asleep: false);
            toward = d;
            return game.FoeAt(p)!;
        }
        throw new InvalidOperationException("No room beside the hero.");
    }

    [Fact]
    public void The_round_counts_three_actions_and_the_multiple_attack_penalty_counts_its_strikes()
    {
        var round = new Round();
        Assert.Equal(0, round.AttackPenalty(agile: false));
        round.Strike();
        Assert.Equal(-5, round.AttackPenalty(agile: false));
        Assert.Equal(-4, round.AttackPenalty(agile: true));
        round.Strike();
        Assert.Equal(-10, round.AttackPenalty(agile: false));
        Assert.Equal(-8, round.AttackPenalty(agile: true));
        round.UseReaction();
        round.EndAction();
        round.EndAction();
        Assert.Equal(2, round.Actions);
        round.EndAction();
        Assert.Equal(0, round.Actions);
        Assert.Equal(0, round.AttackPenalty(agile: false));
        Assert.True(round.ReactionReady);
    }

    [Fact]
    public void Only_bridged_monsters_are_placed()
    {
        var bridge = CreatureBridge.Load(TestData.Data);
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var game = FirstLevel(seed);
            Assert.NotEmpty(game.Foes);
            Assert.All(game.Foes, f => Assert.True(bridge.Has(f.Monster.Race)));
        }
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(4, 1)]
    [InlineData(10, 3)]
    [InlineData(30, 8)]
    [InlineData(60, 12)]
    [InlineData(100, 20)]
    public void Creature_level_follows_depth_along_the_curve(int depth, int level) =>
        Assert.Equal(level, CreatureBridge.LevelForDepth(depth));

    [Fact]
    public void A_monster_gets_pathfinder_numbers_for_its_level_and_its_angband_flags()
    {
        var bridge = CreatureBridge.Load(TestData.Data);
        var kobold = TestData.Data.Monster("kobold")!;
        var stats = bridge.StatsFor(kobold);
        var formula = CreatureStats.For(CreatureBridge.LevelForDepth(kobold.Depth), CreatureRank.Normal);
        Assert.InRange(stats.ArmorClass, formula.ArmorClass - 2, formula.ArmorClass + 2);
        Assert.Equal(formula.AttackBonus, stats.AttackBonus);
        Assert.Contains(DamageType.Poison, CreatureBridge.DefensesFor(kobold).Immunities);
        var skeleton = TestData.Data.Monster("skeleton_kobold")!;
        Assert.True(CreatureBridge.DefensesFor(skeleton).Weaknesses.ContainsKey(DamageType.Vitality));
        Assert.Equal(CreatureRank.Elite, bridge.StatsFor(TestData.Data.Monster("bullroarer")!).Rank);
    }

    [Fact]
    public void Walking_into_a_monster_strikes_it_until_it_dies_and_the_hero_learns_from_it()
    {
        var game = FirstLevel(3);
        var foe = Beside(game, "large_white_snake", out var toward);
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        var xp = game.Hero.Experience;
        for (var i = 0; i < 200 && foe.Monster.IsActive && !game.IsOver; i++)
            game.Perform(new Walk(toward));
        Assert.False(foe.Monster.IsActive);
        Assert.Null(game.FoeAt(foe.Monster.Position));
        Assert.Contains(messages, m => m.StartsWith("You hit the large white snake (", StringComparison.Ordinal) || m.StartsWith("You critically hit", StringComparison.Ordinal));
        Assert.Contains("You have slain the large white snake.", messages);
        Assert.True(game.Hero.Experience > xp || game.Hero.Level > 1);
    }

    [Fact]
    public void The_hero_strikes_at_minus_five_then_minus_ten_in_a_round()
    {
        var game = FirstLevel(4);
        var foe = Beside(game, "white_jelly", out var toward);
        // Sturdy enough to last the round.
        foe.Monster.MaxHp = foe.Monster.Hp = 1000;
        var penalties = new List<string>();
        game.Published += e => { if (e is MessageEvent m && m.Text.StartsWith("You ", StringComparison.Ordinal) && m.Text.Contains("vs AC")) penalties.Add(m.Text); };
        while (game.Hero.Round.Actions != 0)
            game.Perform(new Hold());
        for (var i = 0; i < 3 && foe.Monster.IsActive; i++)
            game.Perform(new Walk(toward));
        Assert.DoesNotContain("for the attacks before it", penalties[0]);
        Assert.Contains("−5 for the attacks before it", penalties[1]);
        if (penalties.Count > 2)
            Assert.Contains("−10 for the attacks before it", penalties[2]);
    }

    [Fact]
    public void Second_breath_holds_the_hero_once_then_death_ends_the_game_with_a_line_in_the_book()
    {
        var game = FirstLevel(5);
        var died = new List<DeathRecord>();
        game.Published += e => { if (e is HeroDiedEvent d) died.Add(d.Record); };
        game.HurtHero(10_000, "a test");
        Assert.False(game.IsOver);
        Assert.Equal(1, game.Hero.HitPoints);
        Assert.False(game.Hero.Character.SecondBreath.Ready);
        game.HurtHero(10_000, "a kobold");
        Assert.True(game.IsOver);
        var record = Assert.Single(died);
        Assert.Equal(record, game.Death);
        Assert.Equal("Lantern-sworn, a level 1 Human Fighter, killed by a kobold 50 ft down.", record.Epitaph);
        Assert.False(game.Perform(new Hold()));
    }

    [Fact]
    public void Resting_heals_but_not_with_a_monster_in_view_and_a_flask_heals_at_once()
    {
        var game = FirstLevel(6);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        game.HurtHero(10, "a test");
        var hurt = game.Hero.HitPoints;
        Assert.Null(game.CannotRest());
        for (var i = 0; i < DungeonSession.TurnsToRestFully && game.Hero.HitPoints < game.Hero.MaxHitPoints; i++)
            Assert.True(game.Perform(new Rest()));
        Assert.True(game.Hero.HitPoints > hurt);
        Assert.Equal("You have no need to rest.", game.CannotRest());

        game.HurtHero(10, "a test");
        var before = game.Hero.HitPoints;
        Assert.True(game.Perform(new QuaffFlask()));
        Assert.True(game.Hero.HitPoints > before);
        Assert.Equal(2, game.Hero.Character.Potion.Charges);
    }

    [Fact]
    public void Monsters_hunt_the_hero_down_and_come_to_fight()
    {
        // An awake kobold a few squares off (jackals would lurk while the hero stands in a corridor, as packs do).
        var game = FirstLevel(7);
        var spot = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p) && game.PathTo(p) is { Count: >= 4 and <= 6 });
        game.Spawn(TestData.Data.Monster("kobold")!, spot, asleep: false);
        var foe = game.FoeAt(spot)!;
        var hits = 0;
        game.Published += e => { if (e is StrikeEvent { ByHero: false }) hits++; };
        for (var i = 0; i < 30 && hits == 0 && !game.IsOver; i++)
            game.Perform(new Hold());
        Assert.True(hits > 0);
        Assert.True(foe.Monster.Position.ChebyshevTo(game.Hero.Position) <= 1 || !foe.Monster.IsActive);
    }

    [Fact]
    public void A_bot_fighter_dives_to_depth_five_in_most_games()
    {
        // With the whole bestiary (spellcasters, breeders, the early uniques) about three games in four get there.
        var reached = 0;
        foreach (var seed in Enumerable.Range(1, 16).Select(s => (ulong)s))
        {
            var game = new DungeonSession(TestData.Data, seed);
            new DiveBot(game).PlayTo(5, maxTurns: 8000);
            if (game.Depth >= 5)
                reached++;
        }
        Assert.True(reached >= 10, $"Only {reached} of 16 bot games reached depth 5.");
    }

    [Fact]
    public void A_game_plays_again_exactly_from_its_seed()
    {
        static (int, Loc, int, long) Play()
        {
            var game = new DungeonSession(TestData.Data, 21);
            new DiveBot(game).PlayTo(3, maxTurns: 1500);
            return (game.Depth, game.Hero.Position, game.Hero.HitPoints, game.Turn);
        }
        Assert.Equal(Play(), Play());
    }
}
