using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using WorldOfAngBand.App.Rendering;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App;

/// <summary>
/// The game window: the isometric view, and the keys and clicks that move the hero. Keys move the hero the way they
/// point on screen (up is up the screen), since the map is turned forty-five degrees.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly Queue<Direction> _travel = new();
    private readonly DispatcherTimer _frames;

    public MainWindow(DungeonSession session)
    {
        Title = "AVAWorldOfAngBand";
        Width = 1280;
        Height = 800;
        MinWidth = 640;
        MinHeight = 400;
        Background = new SolidColorBrush(Color.FromRgb(9, 8, 13));
        View = new IsoView { Session = session };
        Content = View;
        Session = session;

        KeyDown += (_, e) => e.Handled = Press(e.Key, e.KeyModifiers);
        TextInput += (_, e) => e.Handled = Type(e.Text);
        View.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(View).Properties.IsLeftButtonPressed)
                TravelTo(View.ToMap(e.GetPosition(View)));
        };
        View.PointerWheelChanged += (_, e) => View.ZoomLevel += Math.Sign(e.Delta.Y);
        // The torches flicker and the hero breathes, so the view is redrawn every frame; a journey takes a step whenever
        // the last one has been drawn.
        _frames = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _frames.Start();
        Closed += (_, _) => _frames.Stop();
    }

    public IsoView View { get; }

    public DungeonSession Session { get; }

    /// <summary>Squares still to walk on the current journey.</summary>
    public int StepsLeft => _travel.Count;

    /// <summary>The map direction a key moves the hero, by where it points on screen; null for other keys.</summary>
    public static Direction? DirectionFor(Key key) => key switch
    {
        Key.Up or Key.NumPad8 or Key.K => Direction.NorthWest,
        Key.PageUp or Key.NumPad9 or Key.U => Direction.North,
        Key.Right or Key.NumPad6 or Key.L => Direction.NorthEast,
        Key.PageDown or Key.NumPad3 or Key.N => Direction.East,
        Key.Down or Key.NumPad2 or Key.J => Direction.SouthEast,
        Key.End or Key.NumPad1 or Key.B => Direction.South,
        Key.Left or Key.NumPad4 or Key.H => Direction.SouthWest,
        Key.Home or Key.NumPad7 or Key.Y => Direction.West,
        _ => null,
    };

    /// <summary>A key pressed: true when the game used it.</summary>
    public bool Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
            return false;
        if (DirectionFor(key) is { } direction)
        {
            _travel.Clear();
            Do(new Walk(direction));
            return true;
        }
        if (key is Key.NumPad5 or Key.Clear)
        {
            _travel.Clear();
            Do(new Hold());
            return true;
        }
        if (key == Key.Escape)
        {
            _travel.Clear();
            return true;
        }
        return false;
    }

    /// <summary>Typed text: '&gt;' and '&lt;' take the stairs, as in Angband; ',' stays put a turn.</summary>
    public bool Type(string? text)
    {
        switch (text)
        {
            case ">":
                _travel.Clear();
                Do(new TakeStairs(Down: true));
                return true;
            case "<":
                _travel.Clear();
                Do(new TakeStairs(Down: false));
                return true;
            case ",":
                _travel.Clear();
                Do(new Hold());
                return true;
        }
        return false;
    }

    /// <summary>Sets off for a square the hero knows, a step each time the last one has been drawn.</summary>
    public bool TravelTo(Loc target)
    {
        _travel.Clear();
        IReadOnlyList<Direction>? path;
        lock (View.Gate)
            path = Session.PathTo(target);
        if (path is null)
            return false;
        foreach (var step in path)
            _travel.Enqueue(step);
        return true;
    }

    /// <summary>One frame: the next step of a journey if the hero has finished the last, and a redraw.</summary>
    public void Tick()
    {
        if (_travel.Count > 0 && !View.Sliding)
        {
            var step = _travel.Dequeue();
            var at = Session.Hero.Position;
            Do(new Walk(step));
            // A door on the way opens and the step is tried again; anything else that stops the hero ends the journey.
            if (Session.Hero.Position == at)
            {
                var door = at.Step(step);
                if (Session.Level.InBounds(door) && Session.Level.IsDoor(door) && Session.Level.IsPassable(door))
                    Do(new Walk(step));
                if (Session.Hero.Position == at)
                    _travel.Clear();
            }
        }
        View.InvalidateVisual();
    }

    private void Do(GameCommand command)
    {
        lock (View.Gate)
            Session.Perform(command);
    }
}
