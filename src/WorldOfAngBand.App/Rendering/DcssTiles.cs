using SkiaSharp;
using WorldOfAngBand.Data.Tiles;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// The Dungeon Crawl Stone Soup tiles (CC0) that AVABand ships, read through its tileset manifest: the classic tile
/// view's art, and the paper fallback for monsters with no Flare sprite (the spec's section 8).
/// </summary>
public sealed class DcssTiles
{
    private static readonly Lazy<DcssTiles?> SharedInstance = new(() =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "tilesets", "dcss");
        return File.Exists(Path.Combine(directory, TilesetManifest.FileName)) ? new DcssTiles(TilesetManifest.Load(directory)) : null;
    });

    private readonly TilesetManifest _manifest;
    private readonly Dictionary<string, SKImage?> _images = [];

    private DcssTiles(TilesetManifest manifest) => _manifest = manifest;

    /// <summary>The tiles beside the executable, or null when they are missing.</summary>
    public static DcssTiles? Shared => SharedInstance.Value;

    public int TileSize => _manifest.TileWidth;

    /// <summary>The image layers for a key such as <c>monster:kobold</c> or <c>terrain:floor</c>, bottom first; empty when there is none.</summary>
    public IReadOnlyList<SKImage> Layers(string key)
    {
        if (!_manifest.Tiles.TryGetValue(key, out var entry))
            return [];
        var images = new List<SKImage>();
        foreach (var layer in entry.Default)
        {
            if (layer.File is not { } file)
                continue;
            if (!_images.TryGetValue(file, out var image))
            {
                var path = _manifest.ResolvePath(file);
                image = File.Exists(path) ? SKImage.FromEncodedData(path)?.ToRasterImage(ensurePixelData: true) : null;
                _images[file] = image;
            }
            if (image is not null)
                images.Add(image);
        }
        return images;
    }
}
