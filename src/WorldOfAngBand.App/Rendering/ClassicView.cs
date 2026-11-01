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
using Rect = Avalonia.Rect;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// AVABand's classic views, kept beside the isometric one (the spec's section 8): the map seen from above, as Angband's
/// letters in their colours or as DCSS tiles. The game is the same; only the drawing changes.
/// </summary>
public sealed class ClassicView : Control
{
    private readonly Dictionary<string, SKColor> _colours = [];

    public ClassicView(ViewState state, HudPainter hud, object gate)
    {
        State = state;
        Hud = hud;
        Gate = gate;
        ClipToBounds = true;
        Focusable = true;
        state.Changed += InvalidateVisual;
    }

    public ViewState State { get; }

    public HudPainter Hud { get; }

    public object Gate { get; }

    /// <summary>Tiles (DCSS) rather than letters.</summary>
    public bool Tiles { get; set; }

    /// <summary>Pixels per square on screen.</summary>
    public float Cell => Tiles ? 32 : 18;

    public override void Render(DrawingContext context) => context.Custom(new DrawOp(this, new Rect(Bounds.Size)));

    private sealed class DrawOp(ClassicView owner, Rect bounds) : ICustomDrawOperation
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

    /// <summary>The square under a point on screen.</summary>
    public Loc ToMap(Point screen)
    {
        if (State.Session is not { } session)
            return default;
        var hero = session.Hero.Position;
        return new Loc(hero.X + (int)Math.Floor((screen.X - Bounds.Width / 2) / Cell + 0.5),
            hero.Y + (int)Math.Floor((screen.Y - Bounds.Height / 2) / Cell + 0.5));
    }

    private SKColor Colour(DungeonSession session, string name)
    {
        if (_colours.TryGetValue(name, out var colour))
            return colour;
        colour = session.Data.Colors.TryGetValue(name, out var hex) && SKColor.TryParse(hex, out var parsed) ? parsed : SKColors.White;
        _colours[name] = colour;
        return colour;
    }

    internal void Draw(SKCanvas canvas, SKSize size)
    {
        canvas.Clear(SKColors.Black);
        if (State.Session is not { } session)
            return;
        State.Prune();
        var level = session.Level;
        var cell = Cell;
        var hero = session.Hero.Position;
        var columns = (int)(size.Width / cell / 2) + 2;
        var rows = (int)(size.Height / cell / 2) + 2;
        using var typeface = SKTypeface.FromFamilyName("DejaVu Sans Mono") ?? SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, cell * 0.95f);
        var tiles = Tiles ? DcssTiles.Shared : null;
        using var memory = new SKPaint { ColorFilter = SKColorFilter.CreateLighting(new SKColor(110, 110, 140), SKColors.Black) };

        SKRect At(Loc p) => SKRect.Create((float)(size.Width / 2 + (p.X - hero.X - 0.5) * cell), (float)(size.Height / 2 + (p.Y - hero.Y - 0.5) * cell), cell, cell);

        void Glyph(Loc p, char glyph, SKColor colour)
        {
            var r = At(p);
            using var ink = new SKPaint { Color = colour, IsAntialias = true };
            canvas.DrawText(glyph.ToString(), r.MidX, r.Bottom - cell * 0.2f, SKTextAlign.Center, font, ink);
        }

        bool Tile(Loc p, string key, SKPaint? paint)
        {
            if (tiles?.Layers(key) is not { Count: > 0 } layers)
                return false;
            foreach (var image in layers)
                canvas.DrawImage(image, At(p), new SKSamplingOptions(SKFilterMode.Nearest), paint);
            return true;
        }

        for (var y = hero.Y - rows; y <= hero.Y + rows; y++)
        for (var x = hero.X - columns; x <= hero.X + columns; x++)
        {
            var p = new Loc(x, y);
            if (!level.InBounds(p) || !session.Known.IsKnown(p))
                continue;
            var terrain = level.Terrain[session.Known.Feature(p)];
            var seen = level[p].Has(SquareFlags.Seen);
            if (tiles is not null)
            {
                Tile(p, "terrain:floor", seen ? null : memory);
                if (terrain.Id != "floor")
                    Tile(p, $"terrain:{terrain.Id}", seen ? null : memory);
                continue;
            }
            var colour = Colour(session, terrain.Color);
            Glyph(p, terrain.Glyph, seen ? colour : colour.WithAlpha(110));
        }
        for (var y = hero.Y - rows; y <= hero.Y + rows; y++)
        for (var x = hero.X - columns; x <= hero.X + columns; x++)
        {
            var p = new Loc(x, y);
            if (!level.InBounds(p) || !session.Known.IsKnown(p) || session.Known.RememberedObject(p) is not { } item)
                continue;
            if (tiles is not null && ItemArt.TileKeys(item).Any(key => Tile(p, key, level[p].Has(SquareFlags.Seen) ? null : memory)))
                continue;
            var glyph = item.IsGold ? '$' : item.Base.Glyph;
            Glyph(p, glyph, Colour(session, item.Kind.Color ?? item.Base.Color ?? "White"));
        }
        foreach (var foe in session.VisibleFoes())
        {
            var race = foe.Monster.Race;
            if (tiles is null || !Tile(foe.Monster.Position, $"monster:{race.Id}", null))
                Glyph(foe.Monster.Position, race.Glyph, Colour(session, race.Color));
        }
        if (tiles is null || !Tile(hero, "player", null))
            Glyph(hero, '@', SKColors.White);
        Hud.Draw(canvas, size, session, State);
    }
}
