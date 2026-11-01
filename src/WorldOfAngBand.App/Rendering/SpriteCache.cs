using SkiaSharp;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// Sprite frames and tiles pre-scaled to the current zoom. Each is cut out of its sheet and resampled once with a
/// high-quality filter, so drawing it each frame is a plain pixel copy: no per-pixel filtering, which is what
/// makes software (CPU) rendering fast enough. Cutting frames out one by one also stops neighbouring frames in
/// Flare's tightly packed sheets from bleeding into each other.
/// </summary>
internal sealed class SpriteCache(FlareAssets assets, SKColorFilter? grade = null)
{
    private static readonly SKSamplingOptions Resample = new(SKCubicResampler.Mitchell);

    /// <summary>How many scales are kept at once: the world's zoom, the bigger creatures' own sizes, the icon slots.</summary>
    public const int MaxScales = 8;

    // One group of frames per scale. A creature drawn larger (the plague crawler, Mother Sallow) or an icon in a
    // bigger slot is its own scale; when they shared one cache that emptied itself on every change of scale, a single
    // crawler on screen threw away and resampled every tile and sprite twice a frame.
    private readonly Dictionary<int, (Dictionary<(string Sheet, int X, int Y, int W, int H), SKImage> Frames, long Used)> _scales = [];
    private long _uses;

    public int Count => _scales.Values.Sum(g => g.Frames.Count);

    /// <summary>The scales whose frames are held.</summary>
    public int Scales => _scales.Count;

    /// <summary>The frame at <paramref name="scale"/>, cut and resampled the first time it is asked for.</summary>
    public SKImage Get(string sheet, int x, int y, int width, int height, float scale)
    {
        var scaleKey = (int)MathF.Round(scale * 10000);
        if (!_scales.TryGetValue(scaleKey, out var group))
        {
            if (_scales.Count >= MaxScales)
            {
                // Zooming leaves whole scales unused: drop the one used longest ago.
                var stale = _scales.MinBy(kv => kv.Value.Used);
                foreach (var image in stale.Value.Frames.Values)
                    image.Dispose();
                _scales.Remove(stale.Key);
            }
            group = ([], 0);
        }
        _scales[scaleKey] = (group.Frames, ++_uses);
        var key = (sheet, x, y, width, height);
        if (group.Frames.TryGetValue(key, out var cached))
            return cached;

        var source = assets.Image(sheet);
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        using var cut = source.Subset(SKRectI.Create(x, y, width, height)) ?? source;
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var pixmap = bitmap.PeekPixels())
            cut.ScalePixels(pixmap, Resample);
        if (grade is not null)
        {
            // Bake the act's colour grade in, so the frame never pays for it again.
            using var graded = new SKBitmap(info);
            using (var canvas = new SKCanvas(graded))
            using (var paint = new SKPaint { ColorFilter = grade })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(bitmap, 0, 0, paint);
            }
            graded.SetImmutable();
            var gradedImage = SKImage.FromBitmap(graded).ToRasterImage(ensurePixelData: true);
            group.Frames[key] = gradedImage;
            return gradedImage;
        }
        bitmap.SetImmutable();
        var scaled = SKImage.FromBitmap(bitmap).ToRasterImage(ensurePixelData: true);
        group.Frames[key] = scaled;
        return scaled;
    }
}
