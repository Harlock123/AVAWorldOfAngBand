using SkiaSharp;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Combat;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// The HUD over either view: where the hero is, their hit points, flasks, Second Breath, round and level, their
/// conditions, the message log, what is under the pointer, and the Order's book when they die.
/// </summary>
public sealed class HudPainter : IDisposable
{
    public const int MessageLines = 5;

    private static readonly SKColor Ink = new(236, 222, 196);
    private static readonly SKColor Dim = new(170, 160, 146);
    private static readonly SKColor Blood = new(178, 34, 30);
    private static readonly SKColor Ember = new(232, 150, 64);

    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("Inter") ?? SKTypeface.Default;
    private readonly SKFont _title;
    private readonly SKFont _small;
    private readonly SKFont _tiny;
    private readonly SKPaint _shadow = new() { Color = SKColors.Black.WithAlpha(200), IsAntialias = true };

    public HudPainter()
    {
        _title = new SKFont(_typeface, 20);
        _small = new SKFont(_typeface, 14);
        _tiny = new SKFont(_typeface, 12);
    }

    /// <summary>What the pointer is over (a monster's name and numbers), shown beside the pointer; null for nothing.</summary>
    public (SKPoint At, string Text)? Hover { get; set; }

    /// <summary>The last lines of the Order's book, shown under a death.</summary>
    public IReadOnlyList<DeathRecord> Book { get; set; } = [];

    /// <summary>The keys line at the bottom, which differs between the views.</summary>
    public string KeysLine { get; set; } = "";

    public void Draw(SKCanvas canvas, SKSize size, DungeonSession session, ViewState view)
    {
        var depth = session.Depth;
        Text(canvas, IsoView.Stratum(depth), 16, 30, _title, Ink);
        Text(canvas, depth == 0 ? "The town" : $"Level {depth}  ·  {depth * DungeonSession.FeetPerLevel} ft", 16, 52, _small, Dim);
        Text(canvas, $"Turn {session.Turn / 10}", size.Width - 16, 30, _small, Dim, SKTextAlign.Right);
        DrawHero(canvas, size, session);
        DrawMessages(canvas, size, view);
        if (Hover is { } hover)
            Label(canvas, hover.Text, hover.At.X + 16, hover.At.Y + 4);
        Text(canvas, KeysLine, 16, size.Height - 12, _tiny, Dim);
        if (session.Death is { } death)
            DrawDeath(canvas, size, death);
        else if (session.Won && view.ShowEpilogue)
            DrawEpilogue(canvas, size, session.Epilogue);
    }

    private void DrawHero(SKCanvas canvas, SKSize size, DungeonSession session)
    {
        var hero = session.Hero;
        var c = hero.Character;
        float x = size.Width - 276, y = 52;
        using var back = new SKPaint { Color = new SKColor(12, 10, 9, 190), IsAntialias = true };
        canvas.DrawRoundRect(SKRect.Create(x - 12, y - 16, 272, 132), 8, 8, back);

        Text(canvas, $"{c.Name}  ·  level {hero.Level} {c.Ancestry.Name} {c.Class.Name}", x, y, _small, Ink);
        // Hit points, as a bar.
        var share = Math.Clamp(hero.HitPoints / (float)Math.Max(1, hero.MaxHitPoints), 0, 1);
        var bar = SKRect.Create(x, y + 10, 248, 14);
        using (var trough = new SKPaint { Color = new SKColor(40, 14, 12), IsAntialias = true })
            canvas.DrawRoundRect(bar, 4, 4, trough);
        using (var fill = new SKPaint { Color = share > 0.5f ? Blood : share > 0.25f ? new SKColor(200, 90, 30) : new SKColor(230, 40, 30), IsAntialias = true })
            canvas.DrawRoundRect(SKRect.Create(bar.Left, bar.Top, bar.Width * share, bar.Height), 4, 4, fill);
        Text(canvas, $"{hero.HitPoints} / {hero.MaxHitPoints} HP", bar.MidX, bar.Bottom - 2, _tiny, Ink, SKTextAlign.Center);

        // Experience toward the next level.
        var xp = SKRect.Create(x, y + 28, 248, 4);
        using (var trough = new SKPaint { Color = new SKColor(40, 34, 20) })
            canvas.DrawRect(xp, trough);
        using (var fill = new SKPaint { Color = Ember })
            canvas.DrawRect(SKRect.Create(xp.Left, xp.Top, xp.Width * hero.Experience / Hero.ExperiencePerLevel, xp.Height), fill);

        // The round: three pips, filled as actions are taken; the reaction beside them.
        Text(canvas, "Round", x, y + 50, _tiny, Dim);
        for (var i = 0; i < Round.ActionsPerRound; i++)
        {
            using var pip = new SKPaint { IsAntialias = true, Color = i < hero.Round.Actions ? Ember : new SKColor(70, 60, 50) };
            canvas.DrawCircle(x + 52 + i * 16, y + 46, 5, pip);
        }
        Text(canvas, hero.Round.ReactionReady ? "reaction ready" : "reaction used", x + 104, y + 50, _tiny, hero.Round.ReactionReady ? Ink : Dim);

        Text(canvas, $"AC {c.ArmorClass().Total}  ·  attack +{c.AttackModifier().Total}  ·  flasks {c.Potion.Charges}  ·  {hero.Gold} gold", x, y + 70, _tiny, Dim);
        Text(canvas, c.SecondBreath.Ready ? "Second Breath ready" : "Second Breath spent", x, y + 88, _tiny, c.SecondBreath.Ready ? Ink : Blood);
        if (hero.MythicTier > 0)
            Text(canvas, $"mythic tier {hero.MythicTier}", x + 248, y + 88, _tiny, Ink, SKTextAlign.Right);
        var conditions = c.Conditions.Active.Select(a => a.Value > 1 ? $"{a.Kind} {a.Value}" : a.Kind.ToString())
            .Concat(c.Persistent.Types.Select(t => t == DamageType.Poison ? "Poisoned" : $"{t} (persistent)"))
            .Concat(hero.Timed.Keys.Select(t => t switch
            {
                "haste" => "Hasted", "heroism" => "Heroic", "berserk" => "Berserk", "bless" => "Blessed",
                _ when t.StartsWith("resist:", StringComparison.Ordinal) => $"Resisting {t[7..].ToLowerInvariant()}",
                _ => t,
            }))
            .Concat(hero.RecallIn > 0 ? ["Recall"] : [])
            .Concat(hero.DescentIn > 0 ? ["Descending"] : []).ToList();
        if (conditions.Count > 0)
            Text(canvas, string.Join(", ", conditions), x, y + 106, _tiny, new SKColor(220, 140, 120));
    }

    private void DrawMessages(SKCanvas canvas, SKSize size, ViewState view)
    {
        var shown = view.Messages.TakeLast(MessageLines).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var age = view.Time - shown[i].At;
            var alpha = (byte)Math.Clamp(255 - Math.Max(0, age - 6) * 120, 70, 255);
            Text(canvas, shown[i].Text, 16, size.Height - 34 - (shown.Count - 1 - i) * 19, _small, Ink.WithAlpha(alpha));
        }
    }

    /// <summary>Death: the screen darkens, and the Order's book shows the hero's line struck through.</summary>
    private void DrawDeath(SKCanvas canvas, SKSize size, DeathRecord death)
    {
        using var shroud = new SKPaint { Color = new SKColor(6, 4, 4, 210) };
        canvas.DrawRect(SKRect.Create(size), shroud);
        var page = SKRect.Create(size.Width / 2 - 320, size.Height / 2 - 170, 640, 340);
        using (var parchment = new SKPaint { Color = new SKColor(214, 196, 158), IsAntialias = true })
            canvas.DrawRoundRect(page, 6, 6, parchment);
        var ink = new SKColor(52, 34, 24);
        using var heading = new SKFont(_typeface, 24);
        Text(canvas, "The Book of the Lantern-sworn", page.MidX, page.Top + 44, heading, ink, SKTextAlign.Center, shadow: false);
        var lines = Book.Count > 0 ? Book.TakeLast(6).ToList() : [death];
        var top = page.Top + 92;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Epitaph;
            var ly = top + i * 30;
            Text(canvas, line, page.MidX, ly, _small, ink, SKTextAlign.Center, shadow: false);
            // Every name in the book is struck through: none of them came back.
            var width = _small.MeasureText(line);
            using var strike = new SKPaint { Color = ink.WithAlpha(lines[i] == death ? (byte)255 : (byte)150), StrokeWidth = 1.6f, IsAntialias = true };
            canvas.DrawLine(page.MidX - width / 2, ly - 5, page.MidX + width / 2, ly - 5, strike);
        }
        Text(canvas, "Press Enter to send another down.", page.MidX, page.Bottom - 24, _small, ink, SKTextAlign.Center, shadow: false);
    }

    /// <summary>The win: the epilogue on a page, and the choice to go on (past the bottom, to depth 127) or lay down the lantern.</summary>
    private void DrawEpilogue(SKCanvas canvas, SKSize size, IReadOnlyList<string> lines)
    {
        using var shroud = new SKPaint { Color = new SKColor(6, 4, 4, 200) };
        canvas.DrawRect(SKRect.Create(size), shroud);
        var page = SKRect.Create(size.Width / 2 - 360, size.Height / 2 - 230, 720, 460);
        using (var parchment = new SKPaint { Color = new SKColor(222, 206, 168), IsAntialias = true })
            canvas.DrawRoundRect(page, 6, 6, parchment);
        var ink = new SKColor(52, 34, 24);
        using var heading = new SKFont(_typeface, 24);
        Text(canvas, "The Cinder of Ashur", page.MidX, page.Top + 44, heading, ink, SKTextAlign.Center, shadow: false);
        var y = page.Top + 84;
        foreach (var line in lines)
        {
            foreach (var wrapped in Wrap(line, page.Width - 60, _small))
            {
                Text(canvas, wrapped, page.Left + 30, y, _small, ink, shadow: false);
                y += 20;
            }
            y += 6;
        }
        Text(canvas, "Enter: lay down the lantern (your line goes in the book).   Escape: go on, deeper.", page.MidX, page.Bottom - 22,
            _small, ink, SKTextAlign.Center, shadow: false);
    }

    private static IEnumerable<string> Wrap(string text, float width, SKFont font)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(next) > width && line.Length > 0)
            {
                yield return line;
                line = word;
            }
            else
                line = next;
        }
        if (line.Length > 0)
            yield return line;
    }

    private void Label(SKCanvas canvas, string text, float x, float y)
    {
        using var plate = new SKPaint { Color = new SKColor(14, 11, 9, 210), IsAntialias = true };
        var width = _small.MeasureText(text);
        canvas.DrawRoundRect(SKRect.Create(x - 6, y - 15, width + 12, 22), 5, 5, plate);
        Text(canvas, text, x, y, _small, Ink, shadow: false);
    }

    private void Text(SKCanvas canvas, string text, float x, float y, SKFont font, SKColor color, SKTextAlign align = SKTextAlign.Left,
        bool shadow = true)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        if (shadow)
            canvas.DrawText(text, x + 1, y + 1, align, font, _shadow);
        canvas.DrawText(text, x, y, align, font, paint);
    }

    public void Dispose()
    {
        _title.Dispose();
        _small.Dispose();
        _tiny.Dispose();
        _shadow.Dispose();
        _typeface.Dispose();
    }
}
