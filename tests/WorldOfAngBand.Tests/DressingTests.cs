using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Dressing;

namespace WorldOfAngBand.Tests;

public class DressingTests
{
    private static DungeonSession FirstLevel(ulong seed)
    {
        var game = new DungeonSession(TestData.Data, seed);
        foreach (var p in game.Level.AllLocs())
            game.Known.Remember(game.Level, p);
        var stair = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Hero.Position = stair;
        game.Perform(new TakeStairs(Down: true));
        return game;
    }

    [Fact]
    public void Every_open_square_has_ground_and_every_wall_beside_one_shows_a_face()
    {
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var game = FirstLevel(seed);
            var dressed = LevelDressing.Dress(game.Level);
            Assert.Equal(Theme.Barrow, dressed.Theme);
            foreach (var p in game.Level.AllLocs())
            {
                var wall = game.Level.Has(p, TerrainFlags.Wall);
                if (!wall)
                    Assert.NotNull(dressed.Ground(p));
                else if (game.Level.Neighbors(p).Any(n => !game.Level.Has(n, TerrainFlags.Wall)))
                    Assert.NotEmpty(dressed.Objects(p));
            }
        }
    }

    [Fact]
    public void Stairs_and_doors_stand_on_their_squares()
    {
        var game = FirstLevel(5);
        var dressed = LevelDressing.Dress(game.Level);
        foreach (var p in game.Level.FindFeature(TerrainFlags.Stair).Concat(game.Level.FindFeature(TerrainFlags.DoorClosed)))
            Assert.Single(dressed.Objects(p));
    }

    [Fact]
    public void A_level_always_dresses_the_same_way()
    {
        var game = FirstLevel(11);
        var a = LevelDressing.Dress(game.Level);
        var b = LevelDressing.Dress(game.Level);
        foreach (var p in game.Level.AllLocs())
        {
            Assert.Equal(a.Ground(p), b.Ground(p));
            Assert.Equal(a.Objects(p), b.Objects(p));
        }
        Assert.Equal(a.Lights, b.Lights);
    }

    [Fact]
    public void The_town_is_grass_ringed_by_trees()
    {
        var game = new DungeonSession(TestData.Data, 3);
        var dressed = LevelDressing.Dress(game.Level);
        Assert.Equal(Theme.Town, dressed.Theme);
        Assert.Equal("tileset_grassland", dressed.Ground(game.Hero.Position)!.Value.Tileset);
        Assert.All(dressed.Objects(new Loc(0, 0)), piece => Assert.Equal("tileset_grassland", piece.Tileset));
        Assert.Contains(game.Level.AllLocs(), p => game.Level.Has(p, TerrainFlags.Shop) && dressed.Lights.Any(l => l.At == p));
    }
}
