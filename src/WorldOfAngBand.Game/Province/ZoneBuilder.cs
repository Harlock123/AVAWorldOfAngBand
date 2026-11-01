using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game.Dressing;

namespace WorldOfAngBand.Game.Province;

/// <summary>
/// Builds a zone as a level (AshenReliquary's World/TileMap.cs, on Angband's terrain): each cell's legend entry gives
/// its terrain (floor where it can be walked; water, lava and low things in the way can be seen over; the rest is
/// rock) and its art (the ground from <see cref="GroundSets"/> or the entry's own tiles, one picked by AshenReliquary's
/// per-cell hash; then its objects), with shores and lava edges shaped as AshenReliquary shapes them. Exits are gates.
/// Outdoors in daylight every cell is lit and known from the start; a dark zone is lit only by its lights.
/// </summary>
public static class ZoneBuilder
{
    /// <summary>AshenReliquary's TileMap.GroundSets: a ground name's tile ids in a Flare tileset.</summary>
    public static readonly IReadOnlyDictionary<string, int[]> GroundSets = new Dictionary<string, int[]>
    {
        ["grass"] = [.. Enumerable.Range(16, 16)],
        ["path"] = [.. Enumerable.Range(32, 12)],
        ["path_edge"] = [44, 45, 46, 47],
        ["water"] = [.. Enumerable.Range(176, 16)],
        ["bog"] = [.. Enumerable.Range(16, 16)],
        ["ash"] = [.. Enumerable.Range(16, 16)],
        ["lava"] = [3000 + 4 * 15],
        ["embers"] = [3100],
        ["none"] = [],
        ["floor"] = [16, 17, 18, 19],
    };

    /// <summary>The level, its dressing, and where each exit's gate is.</summary>
    public sealed record Built(Level Level, DressedLevel Dressed, IReadOnlyDictionary<string, Loc> Gates);

    public static Built Build(ZoneDef zone, TerrainRegistry terrain, int depth)
    {
        var width = zone.Width + 2;
        var height = zone.Height + 2;
        // A border of rock round the zone, so nothing walks off its edge.
        // Outdoors the hero sees the lie of the land at once, as in AshenReliquary; a dark zone is found as a dungeon is.
        var level = new Level(terrain, width, height, depth) { IsKnown = !zone.IsDark };
        var ground = new Piece?[width * height];
        var objects = new Piece[]?[width * height];
        var lights = new List<DressLight>();
        var water = new bool[width, height];
        var lava = new bool[width, height];
        ushort floor = terrain["floor"].Index, rock = terrain["zone_wall"].Index, low = terrain["zone_obstacle"].Index,
            deep = terrain["zone_water"].Index, molten = terrain["zone_lava"].Index, gate = terrain["zone_gate"].Index;
        foreach (var p in level.AllLocs())
            level.SetFeature(p, rock);
        for (var y = 0; y < zone.Height; y++)
        {
            var row = zone.Rows[y];
            for (var x = 0; x < zone.Width; x++)
            {
                var key = x < row.Length ? row[x].ToString() : " ";
                if (!zone.Legend.TryGetValue(key, out var entry))
                    continue;
                var at = new Loc(x + 1, y + 1);
                var i = at.Y * width + at.X;
                var isWater = entry.Ground == "water";
                var isLava = entry.Ground == "lava";
                water[at.X, at.Y] = isWater;
                lava[at.X, at.Y] = isLava;
                var blocked = entry.Blocks || isWater || (isLava && !entry.Bridge);
                level.SetFeature(at, !blocked ? floor
                    : entry.BlocksSight || (entry.Ground == "none" && entry.Objects is not { Count: > 0 }) ? rock
                    : isWater ? deep
                    : isLava ? molten
                    : low);
                var set = entry.Tiles is { Count: > 0 } own ? own : GroundSets.GetValueOrDefault(entry.Ground) ?? [];
                if (set.Count > 0)
                    ground[i] = new Piece(zone.Tileset, set[Hash(x, y, 0) % set.Count], Tint: entry.Ground == "bog" ? Tint.Bog : Tint.None);
                if (entry.Objects is { Count: > 0 } things)
                    objects[i] = entry.Stack ? [.. things.Select(t => new Piece(zone.Tileset, t))] : [new Piece(zone.Tileset, things[Hash(x, y, 1) % things.Count])];
                if (entry.Light > 0)
                    lights.Add(new DressLight(at, (float)entry.Light, entry.LightColor));
            }
        }
        ShapeShores(zone, ground, water, width, height);
        ShapeLava(zone, ground, lava, width, height);
        // Daylight outdoors; a dark zone is lit only where its lights reach.
        foreach (var p in level.AllLocs())
        {
            if (!zone.IsDark || lights.Any(l => p.DistanceTo(l.At) <= l.Radius))
                level[p].Flags |= SquareFlags.Glow;
        }
        var gates = new Dictionary<string, Loc>();
        foreach (var exit in zone.Exits)
        {
            var at = new Loc(exit.At[0] + 1, exit.At[1] + 1);
            if (!level.InBounds(at))
                continue;
            level.SetFeature(at, gate);
            gates[exit.Id] = at;
        }
        return new Built(level, new DressedLevel(level, ThemeFor(zone), ground, objects, lights), gates);
    }

    /// <summary>
    /// A cache's chest, as AshenReliquary draws it (WorldView.DrawCache): the dungeon set's wooden chests (shut 144, open
    /// 160), the ruins' in the snow (1264, 1266), the cave set's in the ash (160, 176), the grassland's elsewhere (76,
    /// the open one dimmed); a reliquary cache is the skull chest. One on for a chest facing the other way.
    /// </summary>
    public static Piece CachePiece(ZoneDef zone, ZonePlace cache, bool opened)
    {
        var facing = cache.Facing == 1 ? 1 : 0;
        var dungeon = zone.Tileset == "tileset_dungeon";
        var snowy = zone.Tileset == "tileset_oriel";
        var ashen = zone.Tileset == "tileset_cinder";
        var id = cache.Kind == "Reliquary"
            ? (dungeon ? 288 : snowy ? 1384 : ashen ? 266 : 297) + (facing == 0 ? 0 : 2) + (opened ? 1 : 0)
            : dungeon ? (opened ? 160 : 144) + facing : snowy ? (opened ? 1266 : 1264) + facing
            : ashen ? (opened ? 176 : 160) + facing : 76 + facing;
        return new Piece(zone.Tileset, id, Tint: opened && cache.Kind != "Reliquary" && !dungeon && !snowy && !ashen ? Tint.Spent : Tint.None);
    }

    /// <summary>Where a zone's square in AshenReliquary's coordinates is on the level (one in, for the border).</summary>
    public static Loc LevelAt(int[] at) => new(at[0] + 1, at[1] + 1);

    private static Theme ThemeFor(ZoneDef zone) => zone.Tileset switch
    {
        "tileset_dungeon" => Theme.Barrow,
        _ => Theme.Town,
    };

    /// <summary>
    /// AshenReliquary's ShapeShores: water whose four corners aren't all water takes a bank tile by which corners are land
    /// (top, right, bottom, left), with a second variant four on; water with no water corner at all is grass.
    /// </summary>
    private static void ShapeShores(ZoneDef zone, Piece?[] ground, bool[,] water, int width, int height)
    {
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            if (!water[x, y])
                continue;
            // A grid corner is water when the four cells round it are.
            bool Wet(int cx, int cy) => water[cx, cy] && water[cx - 1, cy] && water[cx, cy - 1] && water[cx - 1, cy - 1];
            var top = !Wet(x, y);
            var right = !Wet(x + 1, y);
            var bottom = !Wet(x + 1, y + 1);
            var left = !Wet(x, y + 1);
            int? tile = (top, right, bottom, left) switch
            {
                (false, false, false, false) => null,
                (true, false, false, false) => 160,
                (false, true, false, false) => 161,
                (false, false, true, false) => 162,
                (false, false, false, true) => 163,
                (true, false, false, true) => 144,
                (true, true, false, false) => 145,
                (false, true, true, false) => 146,
                (false, false, true, true) => 147,
                (true, true, false, true) => 152,
                (true, true, true, false) => 153,
                (false, true, true, true) => 154,
                (true, false, true, true) => 155,
                (true, false, true, false) => 153,
                (false, true, false, true) => 154,
                _ => -1,
            };
            var i = y * width + x;
            if (tile == -1)
                ground[i] = new Piece(zone.Tileset, GroundSets["grass"][Hash(x - 1, y - 1, 0) % 16]);
            else if (tile is { } bank)
                ground[i] = new Piece(zone.Tileset, bank + (Hash(x - 1, y - 1, 2) % 2) * 4);
        }
    }

    /// <summary>AshenReliquary's ShapeLava: 3000 + 4 × the lava corners (1 top, 2 right, 4 bottom, 8 left) + the cell's parity; embers where no corner is lava.</summary>
    private static void ShapeLava(ZoneDef zone, Piece?[] ground, bool[,] lava, int width, int height)
    {
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            if (!lava[x, y])
                continue;
            bool Hot(int cx, int cy) => lava[cx, cy] && lava[cx - 1, cy] && lava[cx, cy - 1] && lava[cx - 1, cy - 1];
            var corners = (Hot(x, y) ? 1 : 0) | (Hot(x + 1, y) ? 2 : 0) | (Hot(x + 1, y + 1) ? 4 : 0) | (Hot(x, y + 1) ? 8 : 0);
            var parity = ((x - 1) & 1) + 2 * ((y - 1) & 1);
            ground[y * width + x] = new Piece(zone.Tileset, corners == 0 ? 3100 + parity : 3000 + 4 * corners + parity);
        }
    }

    /// <summary>AshenReliquary's TileMap.Hash: a stable hash of a cell (in the zone's own coordinates) and a salt.</summary>
    internal static int Hash(int x, int y, int salt)
    {
        unchecked
        {
            var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
}
