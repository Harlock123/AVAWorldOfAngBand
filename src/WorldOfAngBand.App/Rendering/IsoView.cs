using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using WorldOfAngBand.Engine.Geometry;
using Rect = Avalonia.Rect;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Dressing;
using WorldOfAngBand.Game.Flare;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// Draws the session isometrically in Flare's art, as AshenReliquary's WorldView does: the ground, then walls, doors,
/// stairs and the hero sorted by depth, then the dark with pools of light, then the depth and the message log. Squares
/// the hero sees are drawn in full, squares only remembered are dimmed, and squares never seen are not drawn.
/// </summary>
public sealed class IsoView : Control
{
    public const float HalfTileWidth = 96f;
    public const float HalfTileHeight = 48f;

    /// <summary>The mouse wheel's zoom steps, furthest out first.</summary>
    public static readonly double[] ZoomSteps = [0.36, 0.45, 0.55, 0.67, 0.82];
    public const int DefaultZoomLevel = 2;

    /// <summary>How long the hero takes to slide from one square to the next.</summary>
    public const double StepSeconds = 0.13;

    /// <summary>How many lines of the message log show.</summary>
    public const int MessageLines = 4;

    /// <summary>
    /// The hero's paper doll, by Flare's slots: until there is equipment, a Lantern-sworn's leathers and longsword
    /// (AshenReliquary's rogue kit, with the fighter's blade).
    /// </summary>
    private static readonly Dictionary<string, string> HeroSlots = new()
    {
        ["head"] = "leather_hood", ["chest"] = "leather_chest", ["legs"] = "leather_pants", ["feet"] = "leather_boots",
        ["hands"] = "leather_gloves", ["main"] = "longsword",
    };

    private readonly FlareAssets _assets = FlareAssets.Shared;
    private readonly SpriteCache _sprites = new(FlareAssets.Shared);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<(string Text, double At)> _messages = [];
    private readonly Dictionary<(Tint Tint, bool Memory), SKPaint?> _paints = [];
    private readonly SKPaint _seeThrough = new() { Color = SKColors.White.WithAlpha(90) };
    private DungeonSession? _session;
    private DressedLevel? _dressed;
    private Loc _slideFrom;
    private double _slideStart = double.NegativeInfinity;
    private int _zoomLevel = DefaultZoomLevel;
    private float _deviceScale = 1f;
    private SKRect _viewport;

    public IsoView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Held while the session changes and while a frame is drawn (Avalonia draws on its render thread).</summary>
    public object Gate { get; } = new();

    /// <summary>A fixed time for the clock-driven art (torch flicker, the hero's breathing), so test pictures repeat.</summary>
    public double? FixedTime { get; set; }

    private double Time => FixedTime ?? _clock.Elapsed.TotalSeconds;

    public DungeonSession? Session
    {
        get => _session;
        set
        {
            if (_session is not null)
                _session.Published -= OnPublished;
            _session = value;
            _messages.Clear();
            if (value is not null)
            {
                value.Published += OnPublished;
                _dressed = LevelDressing.Dress(value.Level);
                _slideStart = double.NegativeInfinity;
            }
            InvalidateVisual();
        }
    }

    public IReadOnlyList<string> Messages => _messages.Select(m => m.Text).ToList();

    public int ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            _zoomLevel = Math.Clamp(value, 0, ZoomSteps.Length - 1);
            InvalidateVisual();
        }
    }

    public double Zoom => ZoomSteps[_zoomLevel];

    /// <summary>True while the hero is still sliding into the square they moved to (never with the clock stopped).</summary>
    public bool Sliding => FixedTime is null && Time - _slideStart < StepSeconds;

    private void OnPublished(IGameEvent e)
    {
        switch (e)
        {
            case MessageEvent m:
                _messages.Add((m.Text, Time));
                if (_messages.Count > 50)
                    _messages.RemoveAt(0);
                break;
            case HeroMovedEvent moved:
                _slideFrom = moved.From;
                _slideStart = Time;
                break;
            case LevelEnteredEvent:
                _dressed = LevelDressing.Dress(_session!.Level);
                _slideStart = double.NegativeInfinity;
                break;
            case DoorOpenedEvent:
                _dressed = LevelDressing.Dress(_session!.Level);
                break;
        }
        InvalidateVisual();
    }

    /// <summary>Where the hero is drawn, in map units: their square's centre, or on the way to it.</summary>
    private (double X, double Y) HeroAt()
    {
        var to = _session!.Hero.Position;
        var t = Sliding ? Math.Clamp((Time - _slideStart) / StepSeconds, 0, 1) : 1;
        return (_slideFrom.X + (to.X - _slideFrom.X) * t + 0.5, _slideFrom.Y + (to.Y - _slideFrom.Y) * t + 0.5);
    }

    public SKPoint ToScreen(double x, double y)
    {
        var (cx, cy) = HeroAt();
        var z = Zoom;
        return new SKPoint((float)(((x - cx) - (y - cy)) * HalfTileWidth * z + Bounds.Width / 2),
            (float)(((x - cx) + (y - cy)) * HalfTileHeight * z + Bounds.Height / 2));
    }

    /// <summary>The square under a point on screen.</summary>
    public Loc ToMap(Point screen)
    {
        var (cx, cy) = HeroAt();
        var a = (screen.X - Bounds.Width / 2) / (HalfTileWidth * Zoom);
        var b = (screen.Y - Bounds.Height / 2) / (HalfTileHeight * Zoom);
        return new Loc((int)Math.Floor(cx + (a + b) / 2), (int)Math.Floor(cy + (b - a) / 2));
    }

    public override void Render(DrawingContext context) => context.Custom(new DrawOp(this, new Rect(Bounds.Size)));

    private sealed class DrawOp(IsoView owner, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null)
                return;
            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            var save = canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            try
            {
                lock (owner.Gate)
                    owner.Draw(canvas, new SKSize((float)bounds.Width, (float)bounds.Height));
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }
    }

    internal void Draw(SKCanvas canvas, SKSize size)
    {
        canvas.Clear(new SKColor(9, 8, 13));
        if (_session is not { } session || _dressed is not { } dressed)
            return;
        _viewport = SKRect.Create(size);
        _deviceScale = Math.Max(0.25f, canvas.TotalMatrix.ScaleX);
        VisibleCells(size, session.Level, out var x0, out var y0, out var x1, out var y1);

        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = new Loc(x, y);
            if (!session.Known.IsKnown(p) || dressed.Ground(p) is not { } ground)
                continue;
            DrawPiece(canvas, ground, ToScreen(x + 0.5, y + 0.5), PaintFor(ground.Tint, Remembered(session.Level, p)));
        }
        DrawDepthSorted(canvas, session, dressed, x0, y0, x1, y1);
        DrawDark(canvas, size, session, dressed, x0, y0, x1, y1);
        DrawShopSigns(canvas, session, x0, y0, x1, y1);
        DrawHud(canvas, size, session);
    }

    private static bool Remembered(Level level, Loc p) => !level[p].Has(SquareFlags.Seen);

    private SKPaint? PaintFor(Tint tint, bool memory)
    {
        if (_paints.TryGetValue((tint, memory), out var paint))
            return paint;
        var colour = tint switch
        {
            Tint.Magma => SKColorFilter.CreateLighting(new SKColor(170, 118, 104), new SKColor(18, 2, 0)),
            Tint.Quartz => SKColorFilter.CreateLighting(new SKColor(205, 212, 232), new SKColor(22, 24, 34)),
            Tint.Lava => SKColorFilter.CreateLighting(new SKColor(255, 130, 70), new SKColor(110, 34, 0)),
            _ => null,
        };
        // What is only remembered is drawn dim and cold, as Angband draws remembered squares darker.
        var dim = memory ? SKColorFilter.CreateLighting(new SKColor(84, 86, 112), new SKColor(2, 2, 8)) : null;
        var filter = colour is null ? dim : dim is null ? colour : SKColorFilter.CreateCompose(dim, colour);
        paint = filter is null ? null : new SKPaint { ColorFilter = filter };
        _paints[(tint, memory)] = paint;
        return paint;
    }

    private void VisibleCells(SKSize size, Level level, out int x0, out int y0, out int x1, out int y1)
    {
        var margin = 520 * Zoom;
        var corners = new[]
        {
            ToMap(new Point(-margin, -margin)), ToMap(new Point(size.Width + margin, -margin)),
            ToMap(new Point(-margin, size.Height + margin * 1.6)), ToMap(new Point(size.Width + margin, size.Height + margin * 1.6)),
        };
        x0 = Math.Max(0, corners.Min(c => c.X));
        y0 = Math.Max(0, corners.Min(c => c.Y));
        x1 = Math.Min(level.Width - 1, corners.Max(c => c.X));
        y1 = Math.Min(level.Height - 1, corners.Max(c => c.Y));
    }

    private readonly record struct DepthItem(double Key, double X, int Order, Piece? Piece, SKPoint At, SKPaint? Paint);

    private static readonly Comparison<DepthItem> DepthOrder = (a, b) =>
        a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.X != b.X ? a.X.CompareTo(b.X) : a.Order.CompareTo(b.Order);

    private readonly List<DepthItem> _depthItems = [];

    private void DrawDepthSorted(SKCanvas canvas, DungeonSession session, DressedLevel dressed, int x0, int y0, int x1, int y1)
    {
        var items = _depthItems;
        items.Clear();
        var (hx, hy) = HeroAt();
        var heroKey = hx + hy;
        var heroFoot = ToScreen(hx, hy);
        var z = (float)Zoom;
        var heroTorso = SKRect.Create(heroFoot.X - 22 * z, heroFoot.Y - 130 * z, 44 * z, 130 * z);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = new Loc(x, y);
            if (!session.Known.IsKnown(p))
                continue;
            var memory = Remembered(session.Level, p);
            var at = ToScreen(x + 0.5, y + 0.5);
            foreach (var piece in dressed.Objects(p))
            {
                if (!_assets.Tileset(piece.Tileset).Tiles.TryGetValue(piece.Id, out var tile))
                    continue;
                var s = z * piece.Scale;
                if (!SKRect.Create(at.X - tile.OffsetX * s, at.Y - tile.OffsetY * s, tile.Width * s, tile.Height * s).IntersectsWith(_viewport))
                    continue;
                // Flat things on the ground (stairs down, rubble) sort under everything standing on them; tall things in
                // front of the hero fade so the hero is never lost behind them.
                var flat = tile.Height * piece.Scale <= 110;
                var key = x + y + (flat ? 0.0 : 1.0);
                var paint = PaintFor(piece.Tint, memory);
                if (!flat && key > heroKey && SKRect.Create(at.X - tile.OffsetX * s, at.Y - tile.OffsetY * s, tile.Width * s, tile.Height * s).IntersectsWith(heroTorso))
                    paint = _seeThrough;
                items.Add(new DepthItem(key, x, items.Count, piece, at, paint));
            }
        }
        items.Add(new DepthItem(heroKey, hx, items.Count, null, heroFoot, null));
        items.Sort(DepthOrder);
        foreach (var item in items)
        {
            if (item.Piece is { } piece)
                DrawPiece(canvas, piece, item.At, item.Paint);
            else
                DrawHero(canvas, session.Hero, item.At);
        }
    }

    private void DrawHero(SKCanvas canvas, Hero hero, SKPoint foot)
    {
        var direction = FlareAssets.SpriteDirection(hero.Facing);
        var moving = Sliding;
        foreach (var slot in _assets.LayerOrder(direction))
        {
            if (!HeroSlots.TryGetValue(slot, out var layer))
                continue;
            var set = _assets.AvatarLayer(layer);
            if (((moving ? set["run"] : null) ?? set["stance"]) is not { } anim)
                continue;
            var t = moving ? Time - _slideStart : Time;
            var frame = anim.Frame(anim.FrameAt(t), direction);
            if (frame.Width == 0)
                continue;
            Blit(canvas, anim.Image ?? set.Image, frame.X, frame.Y, frame.Width, frame.Height, frame.OffsetX, frame.OffsetY, foot, (float)Zoom, null);
        }
    }

    private void DrawPiece(SKCanvas canvas, Piece piece, SKPoint at, SKPaint? paint)
    {
        if (!_assets.Tileset(piece.Tileset).Tiles.TryGetValue(piece.Id, out var tile))
            return;
        var (sx, sy) = AnimationFrame(tile, Time);
        Blit(canvas, tile.Image, sx, sy, tile.Width, tile.Height, tile.OffsetX, tile.OffsetY, at, (float)Zoom * piece.Scale, paint);
    }

    private static (int X, int Y) AnimationFrame(FlareTile tile, double time)
    {
        if (tile.Animation.Count == 0)
            return (tile.X, tile.Y);
        var total = tile.Animation.Sum(f => f.DurationMs);
        var t = (int)(time * 1000) % Math.Max(1, total);
        foreach (var f in tile.Animation)
        {
            if (t < f.DurationMs)
                return (f.X, f.Y);
            t -= f.DurationMs;
        }
        return (tile.X, tile.Y);
    }

    /// <summary>Draws one frame or tile anchored at <paramref name="anchor"/>, from the pre-scaled cache, at a whole pixel.</summary>
    private void Blit(SKCanvas canvas, string sheet, int x, int y, int width, int height, int offsetX, int offsetY,
        SKPoint anchor, float scale, SKPaint? paint)
    {
        var left = anchor.X - offsetX * scale;
        var top = anchor.Y - offsetY * scale;
        if (!SKRect.Create(left, top, width * scale, height * scale).IntersectsWith(_viewport))
            return;
        canvas.Save();
        canvas.Scale(1 / _deviceScale);
        var image = _sprites.Get(sheet, x, y, width, height, scale * _deviceScale);
        canvas.DrawImage(image, MathF.Round(left * _deviceScale), MathF.Round(top * _deviceScale), paint);
        canvas.Restore();
    }

    /// <summary>
    /// The dark: below the town everything lies under a shroud, with pools of light round the hero's torch, the torches
    /// on the walls and whatever else glows. (AshenReliquary's darkness, drawn at a quarter of the size and stretched.)
    /// </summary>
    private void DrawDark(SKCanvas canvas, SKSize size, DungeonSession session, DressedLevel dressed, int x0, int y0, int x1, int y1)
    {
        if (session.Depth == 0)
            return;
        var z = (float)Zoom;
        var time = Time;
        var (hx, hy) = HeroAt();
        var lights = new List<(SKPoint At, float Radius, SKColor Glow)>
        {
            (ToScreen(hx, hy), session.Hero.LightRadius + 2.2f, new SKColor(255, 190, 120, 30)),
        };
        foreach (var l in dressed.Lights)
        {
            if (l.At.X < x0 - 6 || l.At.X > x1 + 6 || l.At.Y < y0 - 6 || l.At.Y > y1 + 6 || !session.Known.IsKnown(l.At))
                continue;
            var fire = l.Color is null;
            var flicker = fire ? (float)(Math.Sin(time * 9.1 + l.At.X * 3.7) * 0.25 + Math.Sin(time * 13.3 + l.At.Y) * 0.15) : 0;
            var glow = fire ? new SKColor(255, 140, 50, 70) : SKColor.Parse(l.Color).WithAlpha(60);
            lights.Add((ToScreen(l.At.X + 0.5, l.At.Y + 0.5), l.Radius + flicker, glow));
        }

        // A lit room the hero can see lies open under its own light, square by square.
        var roomLight = new List<SKPoint>();
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            ref var square = ref session.Level[x, y];
            if (square.Has(SquareFlags.Glow) && square.Has(SquareFlags.Seen))
                roomLight.Add(ToScreen(x + 0.5, y + 0.5));
        }

        const float Down = 4f;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Ceiling(size.Width / Down)), Math.Max(1, (int)Math.Ceiling(size.Height / Down)),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var mask = surface.Canvas;
        mask.Clear(new SKColor(6, 5, 12, 150));
        mask.Save();
        mask.Scale(1 / Down);
        foreach (var at in roomLight)
            Ellipse(mask, at, 1.6f * HalfTileWidth * z, SKBlendMode.DstOut, SKColors.Black.WithAlpha(150));
        foreach (var (at, radius, _) in lights)
            Ellipse(mask, at, radius * HalfTileWidth * z, SKBlendMode.DstOut, SKColors.Black);
        foreach (var (at, radius, glow) in lights)
            Ellipse(mask, at, radius * HalfTileWidth * z * 0.8f, SKBlendMode.SrcOver, glow);
        mask.Restore();
        using var snapshot = surface.Snapshot();
        canvas.DrawImage(snapshot, SKRect.Create(size), new SKSamplingOptions(SKFilterMode.Linear));
    }

    private static void Ellipse(SKCanvas canvas, SKPoint at, float radius, SKBlendMode blend, SKColor color)
    {
        using var paint = new SKPaint
        {
            BlendMode = blend,
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), radius,
                [color, color.WithAlpha((byte)(color.Alpha * 0.55)), color.WithAlpha(0)], [0f, 0.55f, 1f], SKShaderTileMode.Clamp),
        };
        canvas.Save();
        canvas.Translate(at.X, at.Y - radius * 0.12f);
        canvas.Scale(1, 0.62f);
        canvas.DrawCircle(0, 0, radius, paint);
        canvas.Restore();
    }

    /// <summary>Each shop's name over its door, so the town reads at a glance.</summary>
    private void DrawShopSigns(SKCanvas canvas, DungeonSession session, int x0, int y0, int x1, int y1)
    {
        using var typeface = SKTypeface.FromFamilyName("Inter") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 15);
        using var ink = new SKPaint { Color = new SKColor(246, 226, 180), IsAntialias = true };
        using var plate = new SKPaint { Color = new SKColor(14, 11, 9, 190), IsAntialias = true };
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = new Loc(x, y);
            if (session.Level.FeatureAt(p).Shop is not { } shop || !session.Known.IsKnown(p))
                continue;
            var name = Setting.ShopName(shop);
            var at = ToScreen(x + 0.5, y + 0.5);
            var width = font.MeasureText(name);
            var top = at.Y - 300 * (float)Zoom;
            canvas.DrawRoundRect(SKRect.Create(at.X - width / 2 - 8, top - 17, width + 16, 24), 5, 5, plate);
            canvas.DrawText(name, at.X, top, SKTextAlign.Center, font, ink);
        }
    }

    /// <summary>The name of the stretch of the Pyre a depth lies in (the spec's strata).</summary>
    public static string Stratum(int depth) => depth switch
    {
        0 => "Gallowmere",
        <= 20 => "The Lid",
        <= 40 => "The Drowned Choir",
        <= 60 => "The Iron Dead",
        <= 80 => "The Cinder Deeps",
        _ => "The Pyre",
    };

    private void DrawHud(SKCanvas canvas, SKSize size, DungeonSession session)
    {
        using var typeface = SKTypeface.FromFamilyName("Inter") ?? SKTypeface.Default;
        using var title = new SKFont(typeface, 20);
        using var small = new SKFont(typeface, 14);
        using var ink = new SKPaint { Color = new SKColor(236, 222, 196), IsAntialias = true };
        using var dim = new SKPaint { Color = new SKColor(170, 160, 146), IsAntialias = true };
        using var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(200), IsAntialias = true };

        void Text(string text, float x, float y, SKFont font, SKPaint paint, SKTextAlign align = SKTextAlign.Left)
        {
            canvas.DrawText(text, x + 1, y + 1, align, font, shadow);
            canvas.DrawText(text, x, y, align, font, paint);
        }

        var depth = session.Depth;
        Text(Stratum(depth), 16, 30, title, ink);
        Text(depth == 0 ? "The town" : $"Level {depth}  ·  {depth * DungeonSession.FeetPerLevel} ft", 16, 52, small, dim);
        Text($"Turn {session.Turn / 10}", size.Width - 16, 30, small, dim, SKTextAlign.Right);

        var shown = _messages.TakeLast(MessageLines).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var age = Time - shown[i].At;
            var alpha = (byte)Math.Clamp(255 - Math.Max(0, age - 6) * 120, 60, 255);
            using var line = new SKPaint { Color = new SKColor(236, 222, 196, alpha), IsAntialias = true };
            Text(shown[i].Text, 16, size.Height - 40 - (shown.Count - 1 - i) * 20, small, line);
        }
        Text("Arrows or keypad: walk  ·  < >: stairs  ·  click: travel  ·  wheel: zoom", 16, size.Height - 14, small, dim);
    }
}
