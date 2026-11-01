using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using WorldOfAngBand.App.Rendering;
using WorldOfAngBand.App.Services;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App;

/// <summary>How the map is drawn: AshenReliquary's isometric art, or AVABand's classic letters or tiles from above.</summary>
public enum ViewMode
{
    Isometric,
    Letters,
    Tiles,
}

/// <summary>
/// The game window: the view, and the keys and clicks that move the hero. In the isometric view keys move the hero the
/// way they point on screen (up is up the screen), since the map is turned forty-five degrees; in the classic views up
/// is north, as in Angband. A journey or a rest stops when a monster comes into view or the hero is hurt.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly Queue<Direction> _travel = new();
    private readonly DispatcherTimer _frames;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Graveyard _graveyard;
    private bool _resting;

    public MainWindow(DungeonSession session, Graveyard? graveyard = null)
    {
        Title = "AVAWorldOfAngBand";
        Width = 1280;
        Height = 800;
        MinWidth = 640;
        MinHeight = 400;
        Background = new SolidColorBrush(Color.FromRgb(9, 8, 13));
        _graveyard = graveyard ?? Graveyard.Default;
        State = new ViewState(() => _clock.Elapsed.TotalSeconds);
        Hud = new HudPainter();
        View = new IsoView(State, Hud);
        Classic = new ClassicView(State, Hud, View.Gate);
        State.Happened += OnHappened;
        Start(session);
        Mode = ViewMode.Isometric;

        KeyDown += (_, e) => e.Handled = Press(e.Key, e.KeyModifiers);
        TextInput += (_, e) => e.Handled = Type(e.Text);
        foreach (var view in new Control[] { View, Classic })
        {
            view.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(view).Properties.IsLeftButtonPressed)
                    Click(MapAt(e.GetPosition(view)));
            };
            view.PointerMoved += (_, e) => Point(e.GetPosition(view));
            view.PointerExited += (_, _) => Hud.Hover = null;
        }
        View.PointerWheelChanged += (_, e) => View.ZoomLevel += Math.Sign(e.Delta.Y);
        // The torches flicker and everyone breathes, so the view is redrawn every frame; a journey or a rest takes a step
        // whenever the last one has been drawn.
        _frames = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _frames.Start();
        Closed += (_, _) => _frames.Stop();
    }

    public ViewState State { get; }

    public HudPainter Hud { get; }

    public IsoView View { get; }

    public ClassicView Classic { get; }

    public DungeonSession Session { get; private set; } = null!;

    /// <summary>Squares still to walk on the current journey.</summary>
    public int StepsLeft => _travel.Count;

    public bool Resting => _resting;

    public ViewMode Mode
    {
        get => field;
        set
        {
            field = value;
            Classic.Tiles = value == ViewMode.Tiles;
            Content = value == ViewMode.Isometric ? View : Classic;
            Hud.KeysLine = value == ViewMode.Isometric
                ? "Arrows/keypad: walk · < >: stairs · R: rest · q: flask · click: travel · wheel: zoom · Ctrl+T: classic view"
                : "Arrows/keypad: walk (up is north) · < >: stairs · R: rest · q: flask · click: travel · Ctrl+T: next view";
            Redraw();
        }
    }

    private void Start(DungeonSession session)
    {
        _travel.Clear();
        _resting = false;
        Session = session;
        Hud.Book = [];
        State.Session = session;
    }

    /// <summary>A new Lantern-sworn after a death: a new seed, the same game data.</summary>
    public void NewGame(ulong? seed = null) => Start(new DungeonSession(Session.Data, seed ?? (ulong)Environment.TickCount64,
        Session.Content, Session.Bridge));

    private void OnHappened(IGameEvent e)
    {
        switch (e)
        {
            case MonsterAppearedEvent:
            case StrikeEvent { ByHero: false }:
                Stop();
                break;
            case HeroDiedEvent died:
                Stop();
                Hud.Book = _graveyard.Add(died.Record);
                break;
        }
    }

    private void Stop()
    {
        _travel.Clear();
        _resting = false;
    }

    /// <summary>The map direction a key moves the hero in the isometric view, by where it points on screen; null for other keys.</summary>
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

    /// <summary>The map direction a key moves the hero in the classic views: as on Angband's keypad, up is north.</summary>
    public static Direction? ClassicDirectionFor(Key key) => key switch
    {
        Key.Up or Key.NumPad8 or Key.K => Direction.North,
        Key.PageUp or Key.NumPad9 or Key.U => Direction.NorthEast,
        Key.Right or Key.NumPad6 or Key.L => Direction.East,
        Key.PageDown or Key.NumPad3 or Key.N => Direction.SouthEast,
        Key.Down or Key.NumPad2 or Key.J => Direction.South,
        Key.End or Key.NumPad1 or Key.B => Direction.SouthWest,
        Key.Left or Key.NumPad4 or Key.H => Direction.West,
        Key.Home or Key.NumPad7 or Key.Y => Direction.NorthWest,
        _ => null,
    };

    /// <summary>A key pressed: true when the game used it.</summary>
    public bool Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            if (key != Key.T)
                return false;
            Mode = (ViewMode)(((int)Mode + 1) % 3);
            return true;
        }
        if (Session.IsOver)
        {
            if (key is Key.Enter or Key.Return)
                NewGame();
            return true;
        }
        var direction = Mode == ViewMode.Isometric ? DirectionFor(key) : ClassicDirectionFor(key);
        if (direction is { } d)
        {
            Stop();
            Do(new Walk(d));
            return true;
        }
        switch (key)
        {
            case Key.NumPad5 or Key.Clear:
                Stop();
                Do(new Hold());
                return true;
            case Key.Escape:
                Stop();
                return true;
        }
        return false;
    }

    /// <summary>Typed text: '&gt;' and '&lt;' take the stairs, as in Angband; ',' stays put; 'R' rests; 'q' drinks a flask.</summary>
    public bool Type(string? text)
    {
        if (Session.IsOver)
            return false;
        switch (text)
        {
            case ">":
                Stop();
                Do(new TakeStairs(Down: true));
                return true;
            case "<":
                Stop();
                Do(new TakeStairs(Down: false));
                return true;
            case ",":
                Stop();
                Do(new Hold());
                return true;
            case "q":
                Stop();
                Do(new QuaffFlask());
                return true;
            case "R":
                return StartResting();
        }
        return false;
    }

    /// <summary>Rests until healed, or until a monster turns up (Angband's R&amp;).</summary>
    public bool StartResting()
    {
        Stop();
        string? why;
        lock (View.Gate)
            why = Session.CannotRest();
        if (why is not null)
        {
            Do(new Rest());
            return true;
        }
        _resting = true;
        return true;
    }

    private Loc MapAt(Point p) => Mode == ViewMode.Isometric ? View.ToMap(p) : Classic.ToMap(p);

    /// <summary>A click: on a monster beside the hero, strike it; anywhere known, travel there.</summary>
    public void Click(Loc target)
    {
        if (Session.IsOver)
            return;
        var hero = Session.Hero.Position;
        if (target.ChebyshevTo(hero) == 1 && Session.FoeAt(target) is { } foe && Session.Seen(foe))
        {
            Stop();
            Do(new Walk(DirectionExtensions.FromOffset(target.X - hero.X, target.Y - hero.Y)));
            return;
        }
        TravelTo(target);
    }

    /// <summary>The pointer moved: name the monster under it, with its numbers.</summary>
    private void Point(Point p)
    {
        lock (View.Gate)
        {
            var at = MapAt(p);
            Hud.Hover = Session.FoeAt(at) is { } foe && Session.Seen(foe)
                ? (new SKPoint((float)p.X, (float)p.Y),
                    $"{foe.Name}  ·  level {foe.Level}, AC {foe.ArmorClass().Total}, {Math.Max(0, foe.Monster.Hp)}/{foe.Monster.MaxHp} HP"
                    + (foe.Monster.IsAsleep ? ", asleep" : foe.Monster.IsAfraid ? ", afraid" : ""))
                : null;
        }
    }

    /// <summary>Sets off for a square the hero knows, a step each time the last one has been drawn.</summary>
    public bool TravelTo(Loc target)
    {
        Stop();
        IReadOnlyList<Direction>? path;
        lock (View.Gate)
            path = Session.PathTo(target);
        if (path is null)
            return false;
        foreach (var step in path)
            _travel.Enqueue(step);
        return true;
    }

    /// <summary>One frame: the next step of a journey or a rest, if the last has been drawn, and a redraw.</summary>
    public void Tick()
    {
        if (!State.Sliding && !Session.IsOver)
        {
            if (_travel.Count > 0)
            {
                var step = _travel.Dequeue();
                var at = Session.Hero.Position;
                Do(new Walk(step));
                // A door on the way opens and the step is tried again; anything else that stops the hero ends the journey.
                if (Session.Hero.Position == at)
                {
                    var door = at.Step(step);
                    if (Session.Level.InBounds(door) && Session.Level.IsDoor(door) && Session.Level.IsPassable(door) && Session.FoeAt(door) is null)
                        Do(new Walk(step));
                    if (Session.Hero.Position == at)
                        _travel.Clear();
                }
            }
            else if (_resting)
            {
                // Several turns of rest a frame: a full rest takes a few seconds, not a minute.
                for (var i = 0; i < 4 && _resting; i++)
                {
                    bool can;
                    lock (View.Gate)
                        can = Session.CannotRest() is null;
                    if (!can || !Do(new Rest()))
                        _resting = false;
                }
            }
        }
        Redraw();
    }

    private void Redraw()
    {
        if (Mode == ViewMode.Isometric)
            View.InvalidateVisual();
        else
            Classic.InvalidateVisual();
    }

    private bool Do(GameCommand command)
    {
        lock (View.Gate)
            return Session.Perform(command);
    }
}
