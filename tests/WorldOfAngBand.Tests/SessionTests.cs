using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.Tests;

public class SessionTests
{
    private static DungeonSession NewGame(ulong seed = 1234) => new(TestData.Data, seed);

    private static Loc Find(DungeonSession game, TerrainFlags flag) => game.Level.FindFeature(flag).First();

    /// <summary>Walks the hero to a square by the session's own path, failing if there is none.</summary>
    private static void TravelTo(DungeonSession game, Loc target)
    {
        var path = game.PathTo(target) ?? throw new InvalidOperationException($"No way to {target}.");
        foreach (var step in path)
        {
            // A closed door on the way takes one turn to open, then the step is taken.
            var before = game.Hero.Position;
            Assert.True(game.Perform(new Walk(step)));
            if (game.Hero.Position == before)
                Assert.True(game.Perform(new Walk(step)));
        }
        Assert.Equal(target, game.Hero.Position);
    }

    [Fact]
    public void A_game_starts_in_the_town_by_day_with_the_whole_town_known()
    {
        var game = NewGame();
        Assert.Equal(0, game.Depth);
        Assert.True(game.Level.IsLit);
        Assert.True(game.Known.IsKnown(Find(game, TerrainFlags.DownStair)));
        Assert.True(game.Level.IsPassable(game.Hero.Position));
    }

    [Fact]
    public void The_same_seed_makes_the_same_game()
    {
        var a = NewGame(99);
        var b = NewGame(99);
        Assert.Equal(a.Level.ToAscii(), b.Level.ToAscii());
        Assert.Equal(a.Hero.Position, b.Hero.Position);
        Assert.NotEqual(a.Level.ToAscii(), NewGame(100).Level.ToAscii());
    }

    [Fact]
    public void Walking_takes_a_turn_and_walls_take_none()
    {
        var game = NewGame();
        var start = game.Hero.Position;
        var open = DirectionExtensions.Compass.First(d => game.Level.IsPassable(start.Step(d)));
        var turn = game.Turn;
        Assert.True(game.Perform(new Walk(open)));
        Assert.Equal(start.Step(open), game.Hero.Position);
        Assert.Equal(turn + 10, game.Turn);

        // Into the town's outer wall: nothing happens and no time passes.
        var edge = new Loc(1, 1);
        game = NewGame();
        TravelTo(game, game.Level.IsPassable(edge) ? edge : FirstOpenNear(game, edge));
        turn = game.Turn;
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        Assert.False(game.Perform(new Walk(Direction.NorthWest)));
        Assert.Equal(turn, game.Turn);
        Assert.Contains(messages, m => m.EndsWith("in the way.", StringComparison.Ordinal));
    }

    private static Loc FirstOpenNear(DungeonSession game, Loc near) =>
        game.Level.AllLocs().Where(game.Level.IsPassable).Where(p => game.PathTo(p) is not null).MinBy(p => p.ChebyshevTo(near));

    [Fact]
    public void Going_down_from_the_town_and_back_up()
    {
        var game = NewGame();
        var entered = new List<int>();
        game.Published += e => { if (e is LevelEnteredEvent l) entered.Add(l.Depth); };
        Assert.False(game.Perform(new TakeStairs(Down: false)));
        TravelTo(game, Find(game, TerrainFlags.DownStair));
        Assert.True(game.Perform(new TakeStairs(Down: true)));
        Assert.Equal(1, game.Depth);
        Assert.Equal(1, game.Hero.MaxDepth);

        // Arriving by the down stair puts the hero on an up staircase (Angband's connected stairs).
        Assert.True(game.Level.IsStair(game.Hero.Position));
        Assert.True(game.Perform(new TakeStairs(Down: false)));
        Assert.Equal(0, game.Depth);
        Assert.Equal([1, 0], entered);
    }

    [Fact]
    public void The_hero_sees_by_torchlight_and_remembers_what_was_seen()
    {
        var game = NewGame(7);
        TravelTo(game, Find(game, TerrainFlags.DownStair));
        game.Perform(new TakeStairs(Down: true));
        Assert.True(game.Known.IsKnown(game.Hero.Position));
        var known = game.Level.AllLocs().Count(game.Known.IsKnown);
        Assert.InRange(known, 1, game.Level.Width * game.Level.Height / 2);
    }

    [Fact]
    public void Walking_into_a_closed_door_opens_it()
    {
        // Find a game whose first level has a closed door the hero can reach, then walk up to it.
        for (ulong seed = 1; seed < 40; seed++)
        {
            var game = NewGame(seed);
            TravelTo(game, Find(game, TerrainFlags.DownStair));
            game.Perform(new TakeStairs(Down: true));
            foreach (var door in game.Level.FindFeature(TerrainFlags.DoorClosed))
            {
                var beside = DirectionExtensions.Compass.Select(door.Step)
                    .FirstOrDefault(p => game.Level.IsPassable(p) && Reachable(game, p));
                if (beside == default)
                    continue;
                TravelTo(game, beside);
                var toward = DirectionExtensions.FromOffset(door.X - beside.X, door.Y - beside.Y);
                Assert.True(game.Perform(new Walk(toward)));
                Assert.Equal(beside, game.Hero.Position);
                Assert.True(game.Level.IsPassable(door));
                return;
            }
        }
        Assert.Fail("No reachable closed door in 40 games.");
    }

    /// <summary>Whether the hero can walk to <paramref name="p"/>, with the whole level made known first.</summary>
    private static bool Reachable(DungeonSession game, Loc p)
    {
        foreach (var q in game.Level.AllLocs())
            game.Known.Remember(game.Level, q);
        return game.PathTo(p) is not null;
    }
}
