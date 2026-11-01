using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Flare;
using WorldOfAngBand.Game.Province;
using WorldOfAngBand.Engine.World;

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
                .Concat((zone.Caches ?? []).SelectMany(c => new[] { ZoneBuilder.CachePiece(zone, c, false), ZoneBuilder.CachePiece(zone, c, true) }))
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
    public void The_west_gate_leads_to_the_fens_and_the_fens_on_to_the_drowned_chapel()
    {
        var game = new DungeonSession(TestData.Data, 112);
        WalkInto(game, game.Gates["west_gate"]);
        Assert.Equal("rotwater_fens", game.Zone?.Id);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        WalkInto(game, game.Gates["chapel"]);
        Assert.Equal("drowned_chapel", game.Zone?.Id);
    }

    [Fact]
    public void Every_zone_can_be_reached_from_gallowmere_and_entered()
    {
        var reached = new HashSet<string> { ProvinceMap.Gallowmere };
        var queue = new Queue<string>([ProvinceMap.Gallowmere]);
        while (queue.Count > 0)
        {
            foreach (var exit in Map[queue.Dequeue()]!.Exits)
            {
                if (exit.To is { } to && Map[to] is not null && reached.Add(to))
                    queue.Enqueue(to);
            }
        }
        Assert.Equal(22, reached.Count);
        var game = new DungeonSession(TestData.Data, 115);
        foreach (var zone in Map.Zones.Where(z => z.Id != ProvinceMap.Gallowmere))
        {
            game.EnterZone(zone.Id, null);
            Assert.Equal(zone.Id, game.Zone?.Id);
            Assert.True(game.Level.IsPassable(game.Hero.Position), zone.Id);
        }
    }

    [Fact]
    public void Every_one_of_ashens_creatures_is_a_bridged_monster_never_met_in_the_deep()
    {
        var game = new DungeonSession(TestData.Data, 116);
        Assert.Equal(58, Map.Creatures.Count);
        foreach (var creature in Map.Creatures)
        {
            var race = TestData.Data.Monster(creature.RaceId);
            Assert.NotNull(race);
            Assert.True(game.Bridge.Has(race!), creature.Id);
            Assert.True(race!.Has(AshenCreature.ProvinceFlag));
        }
        for (var depth = 1; depth <= 40; depth += 13)
        {
            game.JumpTo(depth);
            Assert.DoesNotContain(game.Foes, f => f.Monster.Race.Has(AshenCreature.ProvinceFlag));
        }
    }

    [Fact]
    public void Ashens_packs_stand_where_it_placed_them_at_their_level_and_rank()
    {
        var game = new DungeonSession(TestData.Data, 117);
        game.EnterZone("gallows_road", null);
        var ashen = game.Foes.Where(f => f.Ashen is not null).ToList();
        Assert.True(ashen.Count >= 20, $"{ashen.Count} placed");
        Assert.All(ashen, f => Assert.Equal(f.Ashen!.Level, f.Level));
        // Ten years on, no Herald stands in the province.
        Assert.DoesNotContain(game.Foes, f => f.Ashen?.Rank >= Rules.Creatures.CreatureRank.Herald);
        var road = Map["gallows_road"]!;
        var zombie = ashen.First(f => f.Ashen!.Creature.Id == "fen_zombie");
        Assert.Contains(zombie.Defenses.Immunities, t => t == Rules.Combat.DamageType.Poison);
        Assert.True(zombie.Monster.MaxHp <= Rules.Creatures.CreatureStats.For(road.Level + 1, Rules.Creatures.CreatureRank.Champion).HitPoints);
    }

    [Fact]
    public void An_ashen_creature_strikes_and_shoots_with_its_own_numbers()
    {
        var game = new DungeonSession(TestData.Data, 118);
        game.EnterZone("gallows_road", null);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        game.PlaceAshen("legion_crossbowman", game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.DistanceTo(game.Hero.Position) is >= 3 and <= 5
            && Engine.Combat.ProjectionPath.Projectable(game.Level, p, game.Hero.Position, 10)).First(), 3);
        var archer = game.Foes.Single();
        archer.Monster.Sleep = 0;
        archer.Monster.Aware = true;
        for (var i = 0; i < 30 && !messages.Any(m => m.Contains("shoots at you")); i++)
            game.Spells.TryCast(archer);
        Assert.Contains(messages, m => m.Contains("shoots at you"));
        game.PlaceAshen("fen_zombie", game.Level.Neighbors(game.Hero.Position).First(game.Level.IsEmptyFloor), 2);
        var zombie = game.Foes.Single(f => f.Ashen!.Creature.Id == "fen_zombie");
        for (var i = 0; i < 20 && !messages.Any(m => m.StartsWith("The fen zombie hits you", StringComparison.Ordinal)); i++)
            game.Fight.MonsterMelee(zombie);
        Assert.Contains(messages, m => m.StartsWith("The fen zombie hits you", StringComparison.Ordinal) && m.Contains("bludgeoning"));
    }

    [Fact]
    public void A_slain_pack_stays_dead_until_the_hero_has_been_five_levels_down()
    {
        var game = new DungeonSession(TestData.Data, 119);
        game.EnterZone("gallows_road", null);
        var pack = game.Foes.First(f => f.Ashen is not null).Ashen!.Pack;
        foreach (var f in game.Foes.Where(f => f.Ashen?.Pack == pack).ToList())
            game.Fight.Hurt(f, 100_000);
        game.EnterZone("gallows_road", null);
        Assert.DoesNotContain(game.Foes, f => f.Ashen?.Pack == pack);
        for (var depth = 1; depth <= 5; depth++)
            game.JumpTo(depth);
        game.EnterZone("gallows_road", null);
        Assert.Contains(game.Foes, f => f.Ashen?.Pack == pack);
    }

    [Fact]
    public void A_cache_opens_once_and_its_things_spill_round_it()
    {
        var game = new DungeonSession(TestData.Data, 120);
        game.EnterZone("gallows_road", null);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        var cache = Map["gallows_road"]!.Caches![0];
        var at = ZoneBuilder.LevelAt(cache.At);
        var closed = game.Dressed.Objects(at)[0];
        WalkInto(game, at);
        Assert.Contains(game.Level.Neighbors(at), p => game.Level.Objects.At(p).Count > 0);
        Assert.NotEqual(closed, game.Dressed.Objects(at)[0]);
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        WalkInto(game, at);
        Assert.Contains("It is empty.", messages);
        game.EnterZone("gallows_road", null);
        Assert.NotEqual(closed, game.Dressed.Objects(at)[0]);
    }

    [Fact]
    public void A_shrine_offers_three_blessings_once()
    {
        var game = new DungeonSession(TestData.Data, 121);
        game.EnterZone("gallows_road", null);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        var shrine = Map["gallows_road"]!.Shrines![0];
        var at = ZoneBuilder.LevelAt(shrine.At);
        ShrineEvent? offered = null;
        game.Published += e => { if (e is ShrineEvent s) offered = s; };
        WalkInto(game, at);
        Assert.NotNull(offered);
        Assert.Equal(3, offered!.Blessings.Distinct().Count());
        game.HurtHero(5, "a test");
        Assert.True(game.Perform(new TakeBlessing(shrine.Id, "heal")));
        Assert.Equal(game.Hero.MaxHitPoints, game.Hero.HitPoints);
        Assert.Equal("zone_shrine_spent", game.Level.FeatureAt(at).Id);
        Assert.False(game.Perform(new TakeBlessing(shrine.Id, "heal")));
    }

    [Theory]
    [InlineData("ironhold", "weaponsmith")]
    [InlineData("saint_oriels_rest", "alchemist")]
    [InlineData("ember_gate", "general")]
    public void A_waystation_has_one_trader(string zone, string store)
    {
        var game = new DungeonSession(TestData.Data, 122);
        game.EnterZone(zone, null);
        Assert.Contains(game.Level.AllLocs(), p => game.Level.FeatureAt(p).Shop == store);
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
