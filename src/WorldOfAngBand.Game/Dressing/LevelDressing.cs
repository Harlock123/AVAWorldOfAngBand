using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.World;

namespace WorldOfAngBand.Game.Dressing;

/// <summary>How a piece of art is coloured to show what it is made of.</summary>
public enum Tint
{
    None,
    /// <summary>A magma vein: the wall darkened and reddened.</summary>
    Magma,
    /// <summary>A quartz vein: the wall paled and cooled.</summary>
    Quartz,
    /// <summary>Lava: the floor burning orange.</summary>
    Lava,
}

/// <summary>One tile of Flare art: which tileset, which tile, and how it is drawn.</summary>
public readonly record struct Piece(string Tileset, int Id, float Scale = 1f, Tint Tint = Tint.None);

/// <summary>A light the level gives of itself: a torch, a glowing vein, lava, a shop's lantern.</summary>
public readonly record struct DressLight(Loc At, float Radius, string? Color = null);

/// <summary>A level dressed in Flare's art: each square's ground, what stands on it, and the level's own lights.</summary>
public sealed class DressedLevel
{
    private readonly Piece?[] _ground;
    private readonly Piece[]?[] _objects;

    internal DressedLevel(Level level, Theme theme, Piece?[] ground, Piece[]?[] objects, IReadOnlyList<DressLight> lights)
    {
        Level = level;
        Theme = theme;
        _ground = ground;
        _objects = objects;
        Lights = lights;
    }

    public Level Level { get; }

    public Theme Theme { get; }

    public IReadOnlyList<DressLight> Lights { get; }

    public Piece? Ground(Loc p) => Level.InBounds(p) ? _ground[p.Y * Level.Width + p.X] : null;

    public IReadOnlyList<Piece> Objects(Loc p) => Level.InBounds(p) ? _objects[p.Y * Level.Width + p.X] ?? [] : [];
}

/// <summary>
/// Dresses a level from the terrain Angband's generator made: floors, walls that show a face toward the floor beside
/// them (as AshenReliquary's map scripts build them), doors, stairs, rubble, veins and lava, with torches on the walls of
/// lit rooms. The same level always dresses the same way: every choice comes from the level's seed.
/// </summary>
public static class LevelDressing
{
    private const string Dungeon = "tileset_dungeon";
    private const string Grassland = "tileset_grassland";

    private static readonly int[] Flagstones = [16, 17, 18, 19];
    private static readonly int[] Cracked = [36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47];
    private static readonly int[] Grass = [.. Enumerable.Range(16, 16)];
    private static readonly int[] Trees = [248, 249, 250, 251, 252, 253, 254, 255];
    private static readonly int[] Rubble = [181, 182, 183];
    private static readonly int[] LooseRubble = [176, 177, 178, 179];

    // The faces of a wall, by which of its neighbours are open (AshenReliquary's tools/drowned_chapel_mapgen.py):
    // far walls, with floor to their south-west or south-east on screen, stand full height; the rest are low.
    private static readonly int[] FaceSouthWest = [65, 69, 97];
    private static readonly int[] FaceSouthEast = [64, 68, 96];
    private const int FaceBoth = 73, CornerSouth = 74, NearBoth = 89, NearNorthEast = 80, NearNorthWest = 81, CornerNorth = 88, EdgeEast = 72, EdgeWest = 74;
    private const int TorchSouthWest = 103, TorchSouthEast = 102;
    private const int DoorShut = 281, DoorOpen = 280, StairsDown = 286, StairsUp = 284;

    public const float TorchRadius = 3.5f;

    public static Theme ThemeFor(int depth) => depth == 0 ? Theme.Town : Theme.Barrow;

    public static DressedLevel Dress(Level level)
    {
        var theme = ThemeFor(level.Depth);
        var ground = new Piece?[level.Width * level.Height];
        var objects = new Piece[]?[level.Width * level.Height];
        var lights = new List<DressLight>();

        foreach (var p in level.AllLocs())
        {
            var i = p.Y * level.Width + p.X;
            var terrain = level.FeatureAt(p);
            var hash = Hash(level.Seed, p, 0);
            var floor = theme == Theme.Town
                ? new Piece(Grassland, Grass[hash % (uint)Grass.Length])
                : new Piece(Dungeon, hash % 7 == 0 ? Cracked[hash / 7 % (uint)Cracked.Length] : Flagstones[hash % (uint)Flagstones.Length]);

            if (!Solid(level, p))
            {
                ground[i] = terrain.Has(TerrainFlags.Fiery) ? floor with { Tint = Tint.Lava } : floor;
                if (terrain.Has(TerrainFlags.Fiery))
                    lights.Add(new DressLight(p, 1.6f, "#ff7a30"));
                objects[i] = Furnish(terrain, hash) is { } piece ? [piece] : null;
                if (terrain.Has(TerrainFlags.Shop))
                    lights.Add(new DressLight(p, 3.4f));
                if (terrain.Id == "quest_altar")
                    lights.Add(new DressLight(p, 3.2f, "#8ea6ff"));
                continue;
            }

            // The town's outer wall is a wood of dead trees; its buildings are stone.
            if (theme == Theme.Town && !level.InBoundsFully(p))
            {
                ground[i] = floor;
                objects[i] = [new Piece(Grassland, Trees[hash % (uint)Trees.Length])];
                continue;
            }
            if (theme == Theme.Town)
                ground[i] = floor;

            var tint = terrain.Has(TerrainFlags.Magma) ? Tint.Magma : terrain.Has(TerrainFlags.Quartz) ? Tint.Quartz : Tint.None;
            if (WallFace(level, p, out var torch) is not { } id)
                continue;
            objects[i] = [new Piece(Dungeon, id, Tint: tint)];
            if (torch)
                lights.Add(new DressLight(p, TorchRadius));
            if (terrain.Has(TerrainFlags.Gold))
                lights.Add(new DressLight(p, 1.2f, "#ffd36a"));
        }
        return new DressedLevel(level, theme, ground, objects, lights);
    }

    /// <summary>What stands on an open square: a door, a staircase, rubble.</summary>
    private static Piece? Furnish(TerrainDef terrain, uint hash)
    {
        // A story level's altar: Flare's stone altar.
        if (terrain.Id == "quest_altar")
            return new Piece(Dungeon, 134);
        if (terrain.Has(TerrainFlags.DoorClosed))
            return new Piece(Dungeon, DoorShut);
        if (terrain.Has(TerrainFlags.DoorAny) || terrain.Has(TerrainFlags.Shop))
            return new Piece(Dungeon, DoorOpen);
        if (terrain.Has(TerrainFlags.DownStair))
            return new Piece(Dungeon, StairsDown, 0.5f);
        if (terrain.Has(TerrainFlags.UpStair))
            return new Piece(Dungeon, StairsUp, 0.33f);
        if (terrain.Has(TerrainFlags.Rubble))
            return terrain.Has(TerrainFlags.Passable)
                ? new Piece(Dungeon, LooseRubble[hash % (uint)LooseRubble.Length])
                : new Piece(Dungeon, Rubble[hash % (uint)Rubble.Length]);
        return null;
    }

    /// <summary>
    /// The face a solid square shows toward the open squares beside it, or null when it has none (rock deep in the
    /// wall, never seen). On screen a square's south-west neighbour is the one below it on the map, its south-east the
    /// one to its right. Far walls beside a lit room may carry a torch.
    /// </summary>
    private static int? WallFace(Level level, Loc p, out bool torch)
    {
        torch = false;
        bool Open(int dx, int dy) => !Solid(level, new Loc(p.X + dx, p.Y + dy));
        bool sw = Open(0, 1), se = Open(1, 0), ne = Open(0, -1), nw = Open(-1, 0);
        if (sw && se)
            return FaceBoth;
        if (sw || se)
        {
            var lit = level.InBounds(p + (sw ? new Loc(0, 1) : new Loc(1, 0)))
                && level[p + (sw ? new Loc(0, 1) : new Loc(1, 0))].Has(SquareFlags.Glow);
            if (lit && level.Depth > 0 && (p.X * 7 + p.Y * 3) % 9 == 0)
            {
                torch = true;
                return sw ? TorchSouthWest : TorchSouthEast;
            }
            var faces = sw ? FaceSouthWest : FaceSouthEast;
            return faces[Hash(level.Seed, p, 1) % (uint)faces.Length];
        }
        if (Open(1, 1))
            return CornerSouth;
        if (ne && nw)
            return NearBoth;
        if (ne)
            return NearNorthEast;
        if (nw)
            return NearNorthWest;
        if (Open(-1, -1))
            return CornerNorth;
        // Open only past a corner to the east or west on screen: a narrow edge of wall shows.
        if (Open(1, -1))
            return EdgeEast;
        if (Open(-1, 1))
            return EdgeWest;
        return null;
    }

    /// <summary>Walls, veins, secret doors and the level's edge; doors, rubble and stairs stand on open ground.</summary>
    private static bool Solid(Level level, Loc p) => !level.InBounds(p) || level.Has(p, TerrainFlags.Wall);

    private static uint Hash(ulong seed, Loc p, int salt)
    {
        var h = seed ^ (ulong)(uint)p.X * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)p.Y * 0xC2B2AE3D27D4EB4FUL ^ (ulong)salt * 0x165667B19E3779F9UL;
        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33;
        return (uint)h;
    }
}

/// <summary>Which art a stretch of the dungeon is dressed in (the spec's strata; more come with later milestones).</summary>
public enum Theme
{
    /// <summary>Gallowmere: grass, a wood of dead trees round it, stone buildings.</summary>
    Town,
    /// <summary>The Lid: the Order's old barrow halls, in Flare's dungeon set.</summary>
    Barrow,
}
