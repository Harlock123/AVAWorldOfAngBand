namespace WorldOfAngBand.Game.Flare;

public enum FlareLoop
{
    PlayOnce,
    Looped,
    BackForth,
}

/// <summary>One frame of one direction: the source rectangle and the anchor (the feet) relative to it.</summary>
public readonly record struct FlareFrame(int X, int Y, int Width, int Height, int OffsetX, int OffsetY);

/// <summary>
/// One animation (stance, run, swing...). Frames are indexed [frame, direction], where direction 0 to 7 runs
/// clockwise on screen from west: W, NW, N, NE, E, SE, S, SW.
/// </summary>
public sealed class FlareAnimation(string name, int frameCount, int durationMs, FlareLoop loop, int? activeFrame, FlareFrame[,] frames,
    string? image = null)
{
    public string Name { get; } = name;

    /// <summary>This animation's own sheet, when the set gives each animation one (Flare's minotaur); else the set's.</summary>
    public string? Image { get; } = image;
    public int FrameCount { get; } = frameCount;
    public int DurationMs { get; } = durationMs;
    public FlareLoop Loop { get; } = loop;

    /// <summary>The frame on which a swing or cast lands its effect (Flare's <c>active_frame</c>); the middle by default.</summary>
    public int ActiveFrame { get; } = activeFrame ?? frameCount / 2;

    public double DurationSeconds => DurationMs / 1000.0;

    public FlareFrame Frame(int index, int direction) => frames[index, direction];

    /// <summary>The frame to show <paramref name="seconds"/> into the animation.</summary>
    public int FrameAt(double seconds)
    {
        if (FrameCount <= 1 || DurationMs <= 0)
            return 0;
        var t = seconds / DurationSeconds;
        switch (Loop)
        {
            case FlareLoop.PlayOnce:
                return Math.Min(FrameCount - 1, (int)(t * FrameCount));
            case FlareLoop.Looped:
                return (int)(t % 1.0 * FrameCount) % FrameCount;
            default:
                // Back and forth: 0,1,2,3,2,1,0,1... over one duration for the full sweep.
                var period = 2 * (FrameCount - 1);
                var step = (int)(t % 1.0 * period) % period;
                return step < FrameCount ? step : period - step;
        }
    }
}

/// <summary>
/// A Flare <c>animations/*.txt</c> file: the sprite sheet plus every animation's frames. Two layouts are read: the
/// packed one (a <c>frame=</c> line per frame and direction, as Flare's own art) and the older grid (a top-level
/// <c>render_size</c> and <c>render_offset</c>, and each animation's <c>position</c>: the sheet's column of its first
/// frame, one row per direction), which community creatures such as Danimal's wolves use. An <c>image=</c> line may give
/// its sheet a key (<c>image=…/minotaur_run.png,run</c>), and frames then name the sheet they are cut from in a ninth
/// field (<c>frame=0,0,…,run</c>): Flare's minotaur gives each animation its own sheet that way.
/// </summary>
public sealed class FlareAnimationSet
{
    private FlareAnimationSet(string image, Dictionary<string, FlareAnimation> animations)
    {
        Image = image;
        Animations = animations;
    }

    public string Image { get; }
    public IReadOnlyDictionary<string, FlareAnimation> Animations { get; }

    public FlareAnimation? this[string name] => Animations.GetValueOrDefault(name);

    public static FlareAnimationSet Parse(string text)
    {
        string? image = null;
        var images = new Dictionary<string, string>();
        int[]? renderSize = null, renderOffset = null;
        var animations = new Dictionary<string, FlareAnimation>();
        var pending = new Dictionary<string, (int Frames, int Duration, FlareLoop Loop, int? Active, List<int[]> Lines, int? Position)>();
        var sheetOf = new Dictionary<string, string>();

        foreach (var (section, key, value) in FlareText.Entries(text))
        {
            if (section is null)
            {
                if (key == "image" && value.Split(',') is [var path, var forAnimation])
                {
                    images[forAnimation.Trim()] = path.Trim();
                    image ??= path.Trim();
                }
                else if (key == "image")
                    image = value;
                else if (key == "render_size")
                    renderSize = FlareText.Ints(value);
                else if (key == "render_offset")
                    renderOffset = FlareText.Ints(value);
                continue;
            }
            var a = pending.TryGetValue(section, out var existing) ? existing : (0, 0, FlareLoop.Looped, null, new List<int[]>(), null);
            switch (key)
            {
                case "frames": a.Frames = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "duration": a.Duration = FlareText.Duration(value); break;
                case "type":
                    a.Loop = value switch { "play_once" => FlareLoop.PlayOnce, "back_forth" => FlareLoop.BackForth, _ => FlareLoop.Looped };
                    break;
                // A number, or "all" (a projectile's every frame is active), which has no frame of its own.
                case "active_frame": a.Active = int.TryParse(value.Split(',')[0], out var active) ? active : null; break;
                case "frame":
                    var fields = value.Split(',');
                    a.Lines.Add(FlareText.Ints(string.Join(',', fields.Take(8))));
                    if (fields.Length > 8)
                        sheetOf.TryAdd(section, fields[8].Trim());
                    break;
                case "position": a.Position = FlareText.Ints(value).FirstOrDefault(); break;
            }
            pending[section] = a;
        }

        foreach (var (name, a) in pending)
        {
            var frames = new FlareFrame[Math.Max(1, a.Frames), 8];
            foreach (var v in a.Lines)
            {
                if (v[0] < frames.GetLength(0) && v[1] is >= 0 and < 8)
                    frames[v[0], v[1]] = new FlareFrame(v[2], v[3], v[4], v[5], v[6], v[7]);
            }
            if (a.Lines.Count == 0 && a.Position is { } column && renderSize is [var w, var h, ..])
            {
                var (ox, oy) = renderOffset is [var x, var y, ..] ? (x, y) : (w / 2, h);
                for (var f = 0; f < frames.GetLength(0); f++)
                for (var d = 0; d < 8; d++)
                    frames[f, d] = new FlareFrame((column + f) * w, d * h, w, h, ox, oy);
            }
            animations[name] = new FlareAnimation(name, Math.Max(1, a.Frames), a.Duration, a.Loop, a.Active, frames,
                images.GetValueOrDefault(sheetOf.GetValueOrDefault(name) ?? name));
        }

        return new FlareAnimationSet(image ?? throw new FormatException("Animation file has no image= line."), animations);
    }
}
