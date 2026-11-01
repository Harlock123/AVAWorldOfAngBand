using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WorldOfAngBand.App.Rendering;
using WorldOfAngBand.Data;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.App.Services;

namespace WorldOfAngBand.App.Tests;

public class WindowTests
{
    private static readonly Lazy<GameData> Data = new(() => DataLoader.LoadDefault(includeUserMods: false));

    /// <summary>A game window, shown headless, with the clock stopped so pictures repeat.</summary>
    private static MainWindow Open(ulong seed = 1234)
    {
        var book = Path.Combine(Path.GetTempPath(), "worldofangband-tests", Guid.NewGuid().ToString("N"), "graveyard.json");
        var window = new MainWindow(new DungeonSession(Data.Value, seed), new Graveyard(book)) { Width = 1280, Height = 800 };
        window.State.Frozen = true;
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets a journey finish: each frame takes the next step.</summary>
    private static void Walk(MainWindow window)
    {
        for (var i = 0; i < 2000 && window.StepsLeft > 0; i++)
            window.Tick();
    }

    /// <summary>
    /// The frame as drawn, saved as <paramref name="name"/>.png in the folder WORLDOFANGBAND_SHOTS names, when it is set
    /// (<c>WORLDOFANGBAND_SHOTS=docs/images dotnet test</c> redraws the README's pictures).
    /// </summary>
    private static WriteableBitmap Capture(MainWindow window, string name)
    {
        Pump();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was drawn.");
        if (Environment.GetEnvironmentVariable("WORLDOFANGBAND_SHOTS") is { Length: > 0 } folder)
        {
            var root = FindRepoRoot();
            var path = Path.Combine(Path.IsPathRooted(folder) ? folder : Path.Combine(root, folder), name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }
        return frame;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AVAWorldOfAngBand.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Explores until a lit room is in view: each time, travel to the nearest known floor beside the unknown (most
    /// levels start in a dark corridor, which makes a dull picture).
    /// </summary>
    private static void ExploreToALitRoom(MainWindow window)
    {
        var game = window.Session;
        for (var trip = 0; trip < 60; trip++)
        {
            if (game.Level.AllLocs().Count(p => game.Level[p].Has(Engine.World.SquareFlags.Seen) && game.Level[p].Has(Engine.World.SquareFlags.Glow)) > 30)
                return;
            var frontier = game.Level.AllLocs()
                .Where(p => game.Known.IsKnown(p) && game.Level.IsPassable(p) && p != game.Hero.Position
                    && DirectionExtensions.Compass.Any(d => game.Level.InBounds(p.Step(d)) && !game.Known.IsKnown(p.Step(d))))
                .OrderBy(p => p.DistanceTo(game.Hero.Position))
                .FirstOrDefault(p => game.PathTo(p) is not null);
            if (frontier == default || !window.TravelTo(frontier))
                return;
            Walk(window);
        }
    }

    /// <summary>How many of the frame's pixels are not the empty background: how much art was drawn.</summary>
    private static double DrawnShare(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var pixels = new int[buffer.Size.Width * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        var background = pixels[0];
        return pixels.Count(p => Math.Abs((p & 0xFF) - (background & 0xFF)) + Math.Abs(((p >> 8) & 0xFF) - ((background >> 8) & 0xFF)) > 24)
            / (double)pixels.Length;
    }

    [AvaloniaFact]
    public void The_town_is_drawn_round_the_hero()
    {
        var window = Open();
        var frame = Capture(window, "WorldOfAngBand-town");
        Assert.True(DrawnShare(frame) > 0.5, $"Only {DrawnShare(frame):P0} of the town was drawn.");
        window.Close();
    }

    [AvaloniaFact]
    public void Keys_move_the_hero_the_way_they_point_on_screen()
    {
        Assert.Equal(Direction.NorthWest, MainWindow.DirectionFor(Key.Up));
        Assert.Equal(Direction.SouthEast, MainWindow.DirectionFor(Key.Down));
        Assert.Equal(Direction.NorthEast, MainWindow.DirectionFor(Key.Right));
        Assert.Equal(Direction.SouthWest, MainWindow.DirectionFor(Key.Left));
        Assert.Equal(Direction.North, MainWindow.DirectionFor(Key.NumPad9));

        var window = Open();
        var start = window.Session.Hero.Position;
        var key = new[] { Key.Up, Key.Down, Key.Left, Key.Right }
            .First(k => window.Session.Level.IsPassable(start.Step(MainWindow.DirectionFor(k)!.Value)));
        Assert.True(window.Press(key));
        Assert.Equal(start.Step(MainWindow.DirectionFor(key)!.Value), window.Session.Hero.Position);

        // The point on screen where the hero stands is the hero's square.
        Assert.Equal(window.Session.Hero.Position, window.View.ToMap(new Avalonia.Point(window.View.Bounds.Width / 2, window.View.Bounds.Height / 2 - 4)));
        window.Close();
    }

    [AvaloniaFact]
    public void Click_to_travel_to_the_stairs_then_go_down_into_the_lid()
    {
        var window = Open();
        var stairs = window.Session.Level.FindFeature(TerrainFlags.DownStair).First();
        Assert.True(window.TravelTo(stairs));
        Walk(window);
        Assert.Equal(stairs, window.Session.Hero.Position);
        Assert.True(window.Type(">"));
        Assert.Equal(1, window.Session.Depth);
        Assert.Contains(window.State.Messages, m => m.Text == "You enter a maze of down staircases.");
        Assert.Equal("The Lid", IsoView.Stratum(window.Session.Depth));

        ExploreToALitRoom(window);
        var frame = Capture(window, "WorldOfAngBand-lid");
        Assert.True(DrawnShare(frame) > 0.12, $"Only {DrawnShare(frame):P0} of the level was drawn.");
        window.Close();
    }

    /// <summary>Puts monsters of these races round the hero, awake, and lets a turn pass so they are seen.</summary>
    private static void Surround(MainWindow window, params string[] races)
    {
        var game = window.Session;
        var spots = DirectionExtensions.Compass.Select(d => game.Hero.Position.Step(d).Step(d))
            .Concat(DirectionExtensions.Compass.Select(d => game.Hero.Position.Step(d)))
            .Where(p => game.Level.InBounds(p) && game.Level.IsEmptyFloor(p) && game.PathTo(p) is not null).ToList();
        for (var i = 0; i < races.Length && i < spots.Count; i++)
            game.Spawn(Data.Value.Monster(races[i])!, spots[i], asleep: true);
        window.Press(Key.NumPad5);
    }

    private static MainWindow OnFirstLevel(ulong seed)
    {
        var window = Open(seed);
        var stairs = window.Session.Level.FindFeature(TerrainFlags.DownStair).First();
        window.TravelTo(stairs);
        Walk(window);
        window.Type(">");
        return window;
    }

    [AvaloniaFact]
    public void Monsters_are_drawn_from_their_bridge_entry_and_named_under_the_pointer()
    {
        var window = OnFirstLevel(1234);
        ExploreToALitRoom(window);
        Surround(window, "kobold", "jackal", "giant_yellow_centipede", "rock_lizard", "cutpurse", "skeleton_kobold");
        var game = window.Session;
        Assert.True(game.VisibleFoes().Count() >= 3, "The monsters put round the hero should be in sight.");
        var frame = Capture(window, "WorldOfAngBand-fight");
        Assert.True(DrawnShare(frame) > 0.12);
        var foe = game.VisibleFoes().First();
        var at = window.View.ToScreen(foe.Monster.Position.X + 0.5, foe.Monster.Position.Y + 0.5);
        window.MouseMove(new Avalonia.Point(at.X, at.Y - 4));
        Pump();
        Assert.NotNull(window.Hud.Hover);
        Assert.StartsWith(foe.Name, window.Hud.Hover!.Value.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_T_turns_to_the_classic_letters_then_tiles_where_up_is_north()
    {
        var window = OnFirstLevel(1234);
        ExploreToALitRoom(window);
        Surround(window, "kobold", "jackal");
        Assert.True(window.Press(Key.T, KeyModifiers.Control));
        Assert.Equal(ViewMode.Letters, window.Mode);
        Assert.IsType<ClassicView>(window.Content);
        Capture(window, "WorldOfAngBand-letters");
        Assert.True(window.Press(Key.T, KeyModifiers.Control));
        Assert.Equal(ViewMode.Tiles, window.Mode);
        var frame = Capture(window, "WorldOfAngBand-tiles");
        Assert.True(DrawnShare(frame) > 0.02);
        Assert.Equal(Direction.North, MainWindow.ClassicDirectionFor(Key.Up));
        Assert.True(window.Press(Key.T, KeyModifiers.Control));
        Assert.Equal(ViewMode.Isometric, window.Mode);
        window.Close();
    }

    [AvaloniaFact]
    public void Death_writes_the_hero_into_the_book_and_enter_sends_another_down()
    {
        var window = OnFirstLevel(77);
        var game = window.Session;
        game.HurtHero(10_000, "a kobold");
        game.HurtHero(10_000, "a kobold");
        Assert.True(game.IsOver);
        Pump();
        Capture(window, "WorldOfAngBand-death");
        var record = Assert.Single(window.Hud.Book);
        Assert.Equal("a kobold", record.Killer);
        Assert.False(window.Type(">"));
        Assert.True(window.Press(Key.Enter));
        Assert.NotSame(game, window.Session);
        Assert.False(window.Session.IsOver);
        Assert.Equal(0, window.Session.Depth);
        window.Close();
    }
}
