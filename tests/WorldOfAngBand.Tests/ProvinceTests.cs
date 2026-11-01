using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Flare;
using WorldOfAngBand.Game.Province;

namespace WorldOfAngBand.Tests;

public class ProvinceTests
{
    private static readonly ProvinceMap Map = ProvinceMap.Shared;

    /// <summary>Steps the hero from beside a gate into it.</summary>
    private static void WalkInto(DungeonSession game, Loc gate)
    {
        var beside = game.Level.Neighbors(gate).Where(p => game.Level.IsPassable(p) && game.FoeAt(p) is null)
            .OrderBy(p => p.DistanceTo(gate)).First();
        game.Hero.Position = beside;
        game.Perform(new Walk(DirectionExtensions.FromOffset(gate.X - beside.X, gate.Y - beside.Y)));
    }

    [Fact]
    public void All_of_ashens_zones_load_and_every_cell_is_in_its_legend()
    {
        Assert.Equal(22, Map.Zones.Count);
        foreach (var zone in Map.Zones)
        {
            var unknown = zone.Rows.SelectMany(r => r).Distinct().Where(c => !zone.Legend.ContainsKey(c.ToString())).ToList();
            Assert.True(unknown.Count == 0, $"{zone.Id}: {string.Join("", unknown)}");
        }
    }

    [Fact]
    public void Every_tile_a_zone_uses_is_in_its_tileset()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "WorldOfAngBand.App", "Assets", "Flare", "tilesetdefs");
        foreach (var zone in Map.Zones)
        {
            var tiles = FlareTileset.Parse(File.ReadAllText(Path.Combine(root, zone.Tileset + ".txt"))).Tiles;
            var built = ZoneBuilder.Build(zone, TestData.Data.Terrain, ProvinceMap.DepthFor(zone.Level));
            var missing = built.Level.AllLocs()
                .SelectMany(p => built.Dressed.Objects(p).Concat(built.Dressed.Ground(p) is { } g ? [g] : []))
                .Select(piece => piece.Id).Where(id => !tiles.ContainsKey(id)).Distinct().ToList();
            Assert.True(missing.Count == 0, $"{zone.Id} ({zone.Tileset}): {string.Join(", ", missing.Take(10))}");
        }
    }

    [Fact]
    public void A_zone_is_built_as_ashen_drew_it()
    {
        var fens = Map["rotwater_fens"]!;
        var built = ZoneBuilder.Build(fens, TestData.Data.Terrain, ProvinceMap.DepthFor(fens.Level));
        var level = built.Level;
        Assert.Equal(fens.Width + 2, level.Width);
        // Outdoors by day every square is lit; the start can be walked; water and trees stop the hero.
        Assert.All(level.AllLocs(), p => Assert.True(level[p].Has(Engine.World.SquareFlags.Glow)));
        Assert.True(level.IsPassable(ZoneBuilder.LevelAt(fens.PlayerStart)));
        Assert.Contains(level.AllLocs(), p => level.FeatureAt(p).Id == "zone_water");
        // Its shores are shaped: bank tiles where water meets land.
        Assert.Contains(level.AllLocs(), p => built.Dressed.Ground(p) is { Id: >= 144 and < 168 });
        Assert.Equal(["chapel", "fort"], built.Gates.Keys.Order());
    }

    [Fact]
    public void Gallowmeres_south_gate_leads_to_the_gallows_road_and_back()
    {
        var game = new DungeonSession(TestData.Data, 111);
        Assert.Equal(["gate", "west_gate"], game.Gates.Keys.Order());
        WalkInto(game, game.Gates["gate"]);
        Assert.Equal("gallows_road", game.Zone?.Id);
        Assert.Equal(0, game.Hero.MaxDepth);
        // The hero arrives where Ashen's road meets the fort, beside its way back.
        Assert.True(game.Hero.Position.DistanceTo(game.Gates["fort_gallowmere"]) <= 4);
        WalkInto(game, game.Gates["fort_gallowmere"]);
        Assert.Null(game.Zone);
        Assert.Equal(0, game.Depth);
        Assert.True(game.Hero.Position.ChebyshevTo(game.Gates["gate"]) == 1);
    }

    [Fact]
    public void The_west_gate_leads_to_the_fens_and_the_way_on_is_not_open_yet()
    {
        var game = new DungeonSession(TestData.Data, 112);
        WalkInto(game, game.Gates["west_gate"]);
        Assert.Equal("rotwater_fens", game.Zone?.Id);
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        WalkInto(game, game.Gates["chapel"]);
        Assert.Equal("rotwater_fens", game.Zone?.Id);
        Assert.Contains(messages, m => m.Contains("not open yet"));
    }

    [Fact]
    public void Above_ground_nothing_drops_the_hero_into_the_dungeon()
    {
        var game = new DungeonSession(TestData.Data, 113);
        game.EnterZone("gallows_road", null);
        game.TeleportLevel();
        game.Hero.DescentIn = 1;
        game.Perform(new Hold());
        game.Perform(new Hold());
        Assert.Equal("gallows_road", game.Zone?.Id);
        Assert.Equal(0, game.Hero.MaxDepth);
    }

    [Fact]
    public void A_death_above_ground_is_written_where_it_happened()
    {
        var game = new DungeonSession(TestData.Data, 114);
        game.EnterZone("gallows_road", null);
        game.HurtHero(10_000, "a test");
        game.HurtHero(10_000, "a fen zombie");
        Assert.EndsWith("killed by a fen zombie in Gallows Road.", game.Death!.Epitaph);
    }

    [Fact]
    public void A_zone_stands_for_the_depth_a_hero_of_its_level_reaches()
    {
        Assert.Equal(1, ProvinceMap.DepthFor(1));
        Assert.Equal(10, ProvinceMap.DepthFor(5));
        Assert.Equal(30, ProvinceMap.DepthFor(10));
        Assert.Equal(90, ProvinceMap.DepthFor(20));
    }
}
