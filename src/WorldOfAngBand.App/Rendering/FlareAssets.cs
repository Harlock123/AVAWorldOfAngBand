using Avalonia.Platform;
using SkiaSharp;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game.Flare;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// Loads the Flare art and its definition files from the app's embedded resources, decoding each image once. Paths
/// are relative to Assets/Flare, as Flare's own files write them. (AshenReliquary's loader, trimmed to what is used.)
/// </summary>
public sealed class FlareAssets
{
    private const string Root = "avares://AVAWorldOfAngBand/Assets/Flare/";

    /// <summary>Flare's hero_layers.txt names directions by map compass; this is the sprite index of each name.</summary>
    private static readonly Dictionary<string, int> CompassToSprite = new()
    {
        ["SW"] = 0, ["W"] = 1, ["NW"] = 2, ["N"] = 3, ["NE"] = 4, ["E"] = 5, ["SE"] = 6, ["S"] = 7,
    };

    private static readonly Lazy<FlareAssets> SharedInstance = new(() => new FlareAssets());

    private readonly Dictionary<string, SKImage> _images = [];
    private readonly Dictionary<string, FlareAnimationSet> _animations = [];
    private readonly Dictionary<string, FlareTileset> _tilesets = [];
    private readonly string[][] _layerOrder = new string[8][];

    private FlareAssets()
    {
        foreach (var line in ReadText("hero_layers.txt").Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith("layer=", StringComparison.Ordinal))
                continue;
            var parts = text["layer=".Length..].Split(',');
            if (CompassToSprite.TryGetValue(parts[0], out var direction))
                _layerOrder[direction] = parts[1..];
        }
    }

    public static FlareAssets Shared => SharedInstance.Value;

    /// <summary>The sprite direction (0 to 7) that faces the way of a map direction.</summary>
    public static int SpriteDirection(Direction direction) => direction switch
    {
        Direction.SouthWest => 0,
        Direction.West => 1,
        Direction.NorthWest => 2,
        Direction.North => 3,
        Direction.NorthEast => 4,
        Direction.East => 5,
        Direction.SouthEast => 6,
        _ => 7,
    };

    /// <summary>The order the hero's paper-doll slots are drawn in when facing sprite direction <paramref name="direction"/>.</summary>
    public IReadOnlyList<string> LayerOrder(int direction) => _layerOrder[direction] ?? ["main", "feet", "legs", "hands", "chest", "off", "head"];

    public string ReadText(string path)
    {
        using var stream = AssetLoader.Open(new Uri(Root + path));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public SKImage Image(string path)
    {
        if (_images.TryGetValue(path, out var image))
            return image;
        using var stream = AssetLoader.Open(new Uri(Root + path));
        using var data = SKData.Create(stream);
        // FromEncodedData alone is lazy: Skia would re-decode the whole sheet whenever its cache drops it.
        using var encoded = SKImage.FromEncodedData(data) ?? throw new InvalidDataException($"Could not decode {path}.");
        image = encoded.ToRasterImage(ensurePixelData: true);
        _images[path] = image;
        return image;
    }

    public FlareTileset Tileset(string name)
    {
        if (!_tilesets.TryGetValue(name, out var set))
            _tilesets[name] = set = FlareTileset.Parse(ReadText($"tilesetdefs/{name}.txt"));
        return set;
    }

    public FlareAnimationSet AvatarLayer(string layer) => Animation($"animations/avatar/male/{layer}.txt");

    /// <summary>A Flare creature's animations (wolf, fire_ant, skeleton_weak…).</summary>
    public FlareAnimationSet Enemy(string sprite) => Animation($"animations/enemies/{sprite}.txt");

    private FlareAnimationSet Animation(string path)
    {
        if (!_animations.TryGetValue(path, out var set))
            _animations[path] = set = FlareAnimationSet.Parse(ReadText(path));
        return set;
    }
}
