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

namespace WorldOfAngBand.App.Tests;

public class WindowTests
{
    private static readonly Lazy<GameData> Data = new(() => DataLoader.LoadDefault(includeUserMods: false));

    /// <summary>A game window, shown headless, with the clock stopped so pictures repeat.</summary>
    private static MainWindow Open(ulong seed = 1234)
    {
        var window = new MainWindow(new DungeonSession(Data.Value, seed)) { Width = 1280, Height = 800 };
        window.View.FixedTime = 0.4;
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
        Assert.Contains("You enter a maze of down staircases.", window.View.Messages);
        Assert.Equal("The Lid", IsoView.Stratum(window.Session.Depth));

        ExploreToALitRoom(window);
        var frame = Capture(window, "WorldOfAngBand-lid");
        Assert.True(DrawnShare(frame) > 0.12, $"Only {DrawnShare(frame):P0} of the level was drawn.");
        window.Close();
    }
}
