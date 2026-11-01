using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Game.Dressing;
using WorldOfAngBand.Game.Flare;
using Rect = Avalonia.Rect;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// Draws the session isometrically in Flare's art, as AshenReliquary's WorldView does: the ground, then walls, doors,
/// stairs, monsters and the hero sorted by depth, then the dark with pools of light, then the HUD. Squares the hero
/// sees are drawn in full, squares only remembered are dimmed, and squares never seen are not drawn. Monsters are drawn
/// from their bridge entry: a Flare creature, the paper-doll body, or a DCSS tile standing on the floor.
/// </summary>
public sealed class IsoView : Control
{
    public const float HalfTileWidth = 96f;
    public const float HalfTileHeight = 48f;

    /// <summary>The mouse wheel's zoom steps, furthest out first.</summary>
    public static readonly double[] ZoomSteps = [0.36, 0.45, 0.55, 0.67, 0.82];
    public const int DefaultZoomLevel = 2;

    /// <summary>How tall a paper-fallback monster stands, in unzoomed pixels.</summary>
    public const float CardHeight = 150f;

    private readonly FlareAssets _assets = FlareAssets.Shared;
    private readonly SpriteCache _sprites = new(FlareAssets.Shared);
    private readonly Dictionary<(Tint Tint, bool Memory), SKPaint?> _paints = [];
    private readonly Dictionary<string, SKPaint?> _tints = [];
    private readonly SKPaint _seeThrough = new() { Color = SKColors.White.WithAlpha(90) };
    private DressedLevel? _dressed;
    private int _zoomLevel = DefaultZoomLevel;
    private float _deviceScale = 1f;
    private SKRect _viewport;

    public IsoView(ViewState state, HudPainter hud)
    {
        State = state;
        Hud = hud;
        ClipToBounds = true;
        Focusable = true;
        state.TerrainChanged += Dress;
        state.Changed += InvalidateVisual;
        Dress();
    }

    public ViewState State { get; }

    public HudPainter Hud { get; }

    /// <summary>Held while the session changes and while a frame is drawn (Avalonia draws on its render thread).</summary>
    public object Gate { get; } = new();

    private DungeonSession? Session => State.Session;

    private void Dress()
    {
        _dressed = Session?.Dressed;
        InvalidateVisual();
    }

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

    private (double X, double Y) Camera => Session is { } s ? State.DrawnAt(ViewState.HeroKey, s.Hero.Position) : (0, 0);

    public SKPoint ToScreen(double x, double y)
    {
        var (cx, cy) = Camera;
        var z = Zoom;
        return new SKPoint((float)(((x - cx) - (y - cy)) * HalfTileWidth * z + Bounds.Width / 2),
            (float)(((x - cx) + (y - cy)) * HalfTileHeight * z + Bounds.Height / 2));
    }

    /// <summary>The square under a point on screen.</summary>
    public Loc ToMap(Point screen)
    {
        var (cx, cy) = Camera;
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
        if (Session is not { } session || _dressed is not { } dressed)
            return;
        State.Prune();
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
        DrawFlashes(canvas);
        DrawHealthBars(canvas, session);
        DrawFloats(canvas);
        DrawShopSigns(canvas, session, x0, y0, x1, y1);
        Hud.Draw(canvas, size, session, State);
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
            Tint.Bog => SKColorFilter.CreateLighting(new SKColor(120, 132, 92), new SKColor(6, 10, 4)),
            Tint.Spent => SKColorFilter.CreateLighting(new SKColor(120, 115, 110), SKColors.Black),
            _ => null,
        };
        // What is only remembered is drawn dim and cold, as Angband draws remembered squares darker.
        var dim = memory ? SKColorFilter.CreateLighting(new SKColor(84, 86, 112), new SKColor(2, 2, 8)) : null;
        var filter = colour is null ? dim : dim is null ? colour : SKColorFilter.CreateCompose(dim, colour);
        paint = filter is null ? null : new SKPaint { ColorFilter = filter };
        _paints[(tint, memory)] = paint;
        return paint;
    }

    /// <summary>A monster's colour, from its Angband colour name, laid over its sprite (a kobold's green, a yeek's blue).</summary>
    private SKPaint? TintFor(string colourName)
    {
        if (_tints.TryGetValue(colourName, out var paint))
            return paint;
        if (Session is { } session && session.Data.Colors.TryGetValue(colourName, out var hex) && SKColor.TryParse(hex, out var colour))
        {
            static byte Mix(byte c) => (byte)(255 * 0.45 + c * 0.55);
            paint = new SKPaint { ColorFilter = SKColorFilter.CreateLighting(new SKColor(Mix(colour.Red), Mix(colour.Green), Mix(colour.Blue)), SKColors.Black) };
        }
        _tints[colourName] = paint;
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

    private readonly record struct DepthItem(double Key, double X, int Order, Piece? Piece, SKPoint At, SKPaint? Paint, Action? Draw);

    private static readonly Comparison<DepthItem> DepthOrder = (a, b) =>
        a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.X != b.X ? a.X.CompareTo(b.X) : a.Order.CompareTo(b.Order);

    private readonly List<DepthItem> _depthItems = [];

    private void DrawDepthSorted(SKCanvas canvas, DungeonSession session, DressedLevel dressed, int x0, int y0, int x1, int y1)
    {
        var items = _depthItems;
        items.Clear();
        var (hx, hy) = Camera;
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
                var bounds = SKRect.Create(at.X - tile.OffsetX * s, at.Y - tile.OffsetY * s, tile.Width * s, tile.Height * s);
                if (!bounds.IntersectsWith(_viewport))
                    continue;
                // Flat things on the ground (stairs down, rubble) sort under everything standing on them; tall things in
                // front of the hero fade so the hero is never lost behind them.
                var flat = tile.Height * piece.Scale <= 110;
                var key = x + y + (flat ? 0.0 : 1.0);
                var paint = PaintFor(piece.Tint, memory);
                if (!flat && key > heroKey && bounds.IntersectsWith(heroTorso))
                    paint = _seeThrough;
                items.Add(new DepthItem(key, x, items.Count, piece, at, paint, null));
            }
        }
        void Add(double key, double x, Action draw) => items.Add(new DepthItem(key, x, items.Count, null, default, null, draw));
        foreach (var foe in session.VisibleFoes())
        {
            var f = foe;
            var (mx, my) = State.DrawnAt(f.Monster.Id, f.Monster.Position);
            Add(mx + my, mx, () => DrawFoe(canvas, session, f, ToScreen(mx, my)));
        }
        // What lies on the floor, as the hero last saw it: flat on the ground, under everyone standing there.
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = new Loc(x, y);
            if (session.Known.IsKnown(p) && session.Known.RememberedObject(p) is { } item)
            {
                var memory = Remembered(session.Level, p);
                Add(x + y + 0.2, x, () => DrawItem(canvas, session, item, ToScreen(p.X + 0.5, p.Y + 0.5), memory));
            }
        }
        foreach (var corpse in State.Corpses)
        {
            var c = corpse;
            if (session.Level.InBounds(c.Where) && session.Level[c.Where].Has(SquareFlags.Seen))
                Add(c.Where.X + c.Where.Y + 0.4, c.Where.X, () => DrawCorpse(canvas, session, c));
        }
        Add(heroKey, hx, () => DrawDoll(canvas, Appearance.For(session.Hero, session.ItemBridge), FlareAssets.SpriteDirection(session.Hero.Facing),
            State.Animation(ViewState.HeroKey), heroFoot, 1f, null));
        items.Sort(DepthOrder);
        foreach (var item in items)
        {
            if (item.Piece is { } piece)
                DrawPiece(canvas, piece, item.At, item.Paint);
            else
                item.Draw!();
        }
    }

    /// <summary>An item on the floor: its Flare loot sprite where there is one, else its DCSS tile lying small.</summary>
    private void DrawItem(SKCanvas canvas, DungeonSession session, Engine.Items.Item item, SKPoint at, bool memory)
    {
        var paint = PaintFor(Tint.None, memory);
        if (ItemArt.Loot(item, session.ItemBridge) is { } loot)
        {
            var set = _assets.Loot(loot);
            if (set["power"] is { } anim)
                DrawFrame(canvas, anim.Image ?? set.Image, anim.Frame(anim.FrameCount - 1, 0), at, 0.8f, paint);
            return;
        }
        if (DcssTiles.Shared is not { } tiles)
            return;
        var layers = ItemArt.TileKeys(item).Select(tiles.Layers).FirstOrDefault(l => l.Count > 0);
        if (layers is null)
            return;
        var size = 64 * (float)Zoom;
        var rect = SKRect.Create(at.X - size / 2, at.Y - size * 0.75f, size, size);
        foreach (var image in layers)
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Nearest), paint);
    }

    /// <summary>A monster, by its bridge entry: a Flare creature, the paper doll, or the DCSS card.</summary>
    private void DrawFoe(SKCanvas canvas, DungeonSession session, Foe foe, SKPoint foot)
    {
        var race = foe.Monster.Race;
        var bridged = session.Bridge.For(race);
        var tint = bridged?.Tint == true ? TintFor(race.Color) : null;
        // A ghost is half seen.
        using var ghost = bridged?.Ghost == true ? new SKPaint { Color = SKColors.White.WithAlpha(120), ColorFilter = tint?.ColorFilter } : null;
        DrawMonsterArt(canvas, race.Id, bridged, FlareAssets.SpriteDirection(State.Facing(foe.Monster.Id)),
            State.Animation(foe.Monster.Id), foot, ghost ?? tint, bridged?.Ghost == true ? 0.5f : 1f);
    }

    /// <summary>A monster just slain: its death throes, fading away.</summary>
    private void DrawCorpse(SKCanvas canvas, DungeonSession session, Corpse corpse)
    {
        if (session.Data.Monster(corpse.RaceId) is not { } race)
            return;
        var bridged = session.Bridge.For(race);
        var age = State.Time - corpse.At;
        var fade = (float)Math.Clamp(1 - (age - ViewState.CorpseSeconds * 0.6) / (ViewState.CorpseSeconds * 0.4), 0, 1);
        var tint = bridged?.Tint == true ? TintFor(race.Color) : null;
        using var faded = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * fade)), ColorFilter = tint?.ColorFilter };
        DrawMonsterArt(canvas, race.Id, bridged, FlareAssets.SpriteDirection(corpse.Facing), ("die", age),
            ToScreen(corpse.Where.X + 0.5, corpse.Where.Y + 0.5), faded, fade);
    }

    private void DrawMonsterArt(SKCanvas canvas, string raceId, BridgedMonster? bridged, int direction,
        (string Animation, double Seconds) animation, SKPoint foot, SKPaint? paint, float fade)
    {
        var scale = (float)(bridged?.Scale ?? 1);
        switch (bridged?.Sprite)
        {
            case "doll":
                DrawDoll(canvas, Appearance.For(bridged.Doll), direction, animation, foot, scale, paint);
                break;
            case { } family:
                var set = _assets.Enemy(family);
                if (Pick(set, animation.Animation) is { } anim)
                    DrawFrame(canvas, anim.Image ?? set.Image, anim.Frame(anim.FrameAt(animation.Seconds), direction), foot, scale, paint);
                break;
            default:
                DrawCard(canvas, raceId, animation, foot, scale, fade);
                break;
        }
    }

    /// <summary>
    /// The paper fallback: the monster's DCSS tile standing upright on the floor, with a shadow at its feet. It hops
    /// when it strikes and sinks into the floor when it dies.
    /// </summary>
    private void DrawCard(SKCanvas canvas, string raceId, (string Animation, double Seconds) animation, SKPoint foot, float scale, float fade)
    {
        if (DcssTiles.Shared is not { } tiles || tiles.Layers($"monster:{raceId}") is not { Count: > 0 } layers)
            return;
        var height = CardHeight * (float)Zoom * scale;
        var t = Math.Min(1, animation.Seconds / (animation.Animation == "die" ? ViewState.CorpseSeconds : ViewState.ActionSeconds));
        var hop = animation.Animation == "swing" ? (float)Math.Sin(t * Math.PI) * height * 0.12f : 0;
        var sink = animation.Animation == "die" ? (float)t * height * 0.6f : 0;
        using (var shadow = new SKPaint { Color = SKColors.Black.WithAlpha((byte)(110 * fade)), IsAntialias = true })
            canvas.DrawOval(foot.X, foot.Y, height * 0.32f, height * 0.12f, shadow);
        var rect = SKRect.Create(foot.X - height / 2, foot.Y - height - hop + sink, height, height);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * fade)) };
        canvas.Save();
        // Sinking: the floor hides what has gone below it.
        canvas.ClipRect(SKRect.Create(rect.Left - 4, rect.Top - height, rect.Width + 8, foot.Y + 2 - (rect.Top - height)));
        foreach (var image in layers)
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        canvas.Restore();
    }

    private static FlareAnimation? Pick(FlareAnimationSet set, string name) => set[name] ?? set["stance"];

    private void DrawDoll(SKCanvas canvas, IReadOnlyDictionary<string, string> layers, int direction,
        (string Animation, double Seconds) animation, SKPoint foot, float scale, SKPaint? paint)
    {
        foreach (var slot in _assets.LayerOrder(direction))
        {
            if (!layers.TryGetValue(slot, out var layer))
                continue;
            var set = _assets.AvatarLayer(layer);
            if (Pick(set, animation.Animation) is not { } anim)
                continue;
            DrawFrame(canvas, anim.Image ?? set.Image, anim.Frame(anim.FrameAt(animation.Seconds), direction), foot, scale, paint);
        }
    }

    private void DrawFrame(SKCanvas canvas, string sheet, FlareFrame frame, SKPoint foot, float scale, SKPaint? paint)
    {
        if (frame.Width == 0)
            return;
        Blit(canvas, sheet, frame.X, frame.Y, frame.Width, frame.Height, frame.OffsetX, frame.OffsetY, foot, (float)Zoom * scale, paint);
    }

    /// <summary>A spell element's colour.</summary>
    private static SKColor ElementColour(string? element) => element switch
    {
        "fire" or "plasma" or "meteor" => new SKColor(255, 120, 40),
        "cold" or "ice" => new SKColor(150, 210, 255),
        "elec" => new SKColor(170, 170, 255),
        "acid" => new SKColor(150, 230, 60),
        "pois" => new SKColor(90, 200, 90),
        "light" or "holy_orb" => new SKColor(255, 250, 200),
        "dark" or "nether" => new SKColor(120, 60, 160),
        "water" => new SKColor(60, 120, 230),
        "chaos" or "disen" or "nexus" => new SKColor(220, 80, 220),
        "sound" or "shards" or "force" or "gravity" or "inertia" or "time" => new SKColor(220, 200, 150),
        "arrow" or null => new SKColor(230, 220, 200),
        _ => new SKColor(255, 210, 120),
    };

    /// <summary>Monsters' spells: a bolt's streak flying to the hero, a ball's burst round them, a breath's cone, a status spell's glow.</summary>
    private void DrawFlashes(SKCanvas canvas)
    {
        var z = (float)Zoom;
        foreach (var f in State.Flashes)
        {
            var t = (float)Math.Clamp((State.Time - f.At) / ViewState.FlashSeconds, 0, 1);
            var colour = ElementColour(f.Element);
            var from = ToScreen(f.From.X + 0.5, f.From.Y + 0.5);
            var to = ToScreen(f.To.X + 0.5, f.To.Y + 0.5);
            from.Y -= 70 * z;
            to.Y -= 60 * z;
            using var paint = new SKPaint { IsAntialias = true, Color = colour.WithAlpha((byte)(230 * (1 - t))), BlendMode = SKBlendMode.Plus };
            switch (f.Kind)
            {
                case Engine.Definitions.MonsterSpellKind.Bolt:
                    var head = new SKPoint(from.X + (to.X - from.X) * Math.Min(1, t * 2), from.Y + (to.Y - from.Y) * Math.Min(1, t * 2));
                    paint.StrokeWidth = 4 * z + 2;
                    paint.StrokeCap = SKStrokeCap.Round;
                    canvas.DrawLine(new SKPoint(head.X - (to.X - from.X) * 0.15f, head.Y - (to.Y - from.Y) * 0.15f), head, paint);
                    canvas.DrawCircle(head, 10 * z, paint);
                    break;
                case Engine.Definitions.MonsterSpellKind.Ball or Engine.Definitions.MonsterSpellKind.Storm:
                    canvas.DrawOval(to.X, to.Y + 40 * z, HalfTileWidth * z * (0.6f + 2 * t), HalfTileHeight * z * (0.6f + 2 * t), paint);
                    break;
                case Engine.Definitions.MonsterSpellKind.Breath:
                    using (var path = new SKPath())
                    {
                        var dx = to.X - from.X;
                        var dy = to.Y - from.Y;
                        var length = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
                        var spread = length * 0.45f;
                        path.MoveTo(from);
                        path.LineTo(to.X - dy / length * spread, to.Y + dx / length * spread);
                        path.LineTo(to.X + dy / length * spread, to.Y - dx / length * spread);
                        path.Close();
                        paint.Color = colour.WithAlpha((byte)(170 * (1 - t)));
                        canvas.DrawPath(path, paint);
                    }
                    break;
                default:
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 3 * z + 1;
                    canvas.DrawOval(from.X, from.Y + 60 * z, 50 * z * (1 + t), 22 * z * (1 + t), paint);
                    break;
            }
        }
    }

    /// <summary>A bar over each hurt monster the hero can see.</summary>
    private void DrawHealthBars(SKCanvas canvas, DungeonSession session)
    {
        var z = (float)Zoom;
        using var trough = new SKPaint { Color = new SKColor(20, 8, 8, 200) };
        using var fill = new SKPaint { Color = new SKColor(200, 40, 34) };
        foreach (var foe in session.VisibleFoes())
        {
            var m = foe.Monster;
            if (m.Hp >= m.MaxHp)
                continue;
            var (x, y) = State.DrawnAt(m.Id, m.Position);
            var foot = ToScreen(x, y);
            var scale = (float)(session.Bridge.For(m.Race)?.Scale ?? 1);
            var bar = SKRect.Create(foot.X - 24 * z, foot.Y - 150 * z * scale, 48 * z, 6 * z);
            canvas.DrawRect(bar, trough);
            canvas.DrawRect(SKRect.Create(bar.Left, bar.Top, bar.Width * Math.Clamp(m.Hp / (float)m.MaxHp, 0, 1), bar.Height), fill);
        }
    }

    /// <summary>Numbers floating up from where blows landed: white on monsters, red on the hero, "miss" grey.</summary>
    private void DrawFloats(SKCanvas canvas)
    {
        using var typeface = SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 18);
        using var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(180), IsAntialias = true };
        var z = (float)Zoom;
        foreach (var f in State.Floats)
        {
            var t = (float)Math.Clamp((State.Time - f.At) / ViewState.FloatSeconds, 0, 1);
            var at = ToScreen(f.Where.X + 0.5, f.Where.Y + 0.5);
            var y = at.Y - 120 * z - t * 50;
            var colour = f.Text == "miss" ? new SKColor(190, 186, 176) : f.Hurt ? new SKColor(255, 80, 60) : new SKColor(255, 240, 210);
            using var ink = new SKPaint { Color = colour.WithAlpha((byte)(255 * (1 - t * t))), IsAntialias = true };
            canvas.DrawText(f.Text, at.X + 1, y + 1, SKTextAlign.Center, font, shadow);
            canvas.DrawText(f.Text, at.X, y, SKTextAlign.Center, font, ink);
        }
    }

    private void DrawPiece(SKCanvas canvas, Piece piece, SKPoint at, SKPaint? paint)
    {
        if (!_assets.Tileset(piece.Tileset).Tiles.TryGetValue(piece.Id, out var tile))
            return;
        var (sx, sy) = AnimationFrame(tile, State.Time);
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
    /// on the walls, and the lit rooms the hero can see. (AshenReliquary's darkness, drawn at a quarter size and stretched.)
    /// </summary>
    private void DrawDark(SKCanvas canvas, SKSize size, DungeonSession session, DressedLevel dressed, int x0, int y0, int x1, int y1)
    {
        if (session.Depth == 0)
            return;
        var z = (float)Zoom;
        var time = State.Time;
        var (hx, hy) = Camera;
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
}
