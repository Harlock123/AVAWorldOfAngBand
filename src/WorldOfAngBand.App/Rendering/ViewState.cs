using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// What the views show besides the game itself, followed from the session's events: the message log, who is sliding
/// from one square to the next, who is swinging or reeling, the numbers that float up from a blow, and the bodies of
/// the slain as they fall. Both the isometric and the classic views draw from it.
/// </summary>
public sealed class ViewState
{
    /// <summary>How long a step's slide lasts.</summary>
    public const double StepSeconds = 0.13;

    /// <summary>How long a swing, a flinch or a fall is shown.</summary>
    public const double ActionSeconds = 0.45;

    /// <summary>How long a number floats.</summary>
    public const double FloatSeconds = 1.1;

    /// <summary>How long the slain lie before they are gone.</summary>
    public const double CorpseSeconds = 1.6;

    private readonly Func<double> _clock;
    private readonly List<(string Text, double At)> _messages = [];
    private readonly Dictionary<int, (Loc From, double At)> _slides = [];
    private readonly Dictionary<int, (string Animation, double At)> _actions = [];
    private readonly Dictionary<int, Direction> _facing = [];
    private readonly List<FloatingText> _floats = [];
    private readonly List<Corpse> _corpses = [];
    private readonly List<SpellFlash> _flashes = [];
    private DungeonSession? _session;

    /// <summary>The hero's key in the slide and action tables (monsters use their ids, which start at 1).</summary>
    public const int HeroKey = 0;

    public ViewState(Func<double> clock) => _clock = clock;

    public double Time => Frozen ? FrozenAt : _clock();

    /// <summary>The time a frozen clock shows.</summary>
    public const double FrozenAt = 0.4;

    /// <summary>Whether the epilogue is on screen (after the win, until the player goes on or retires).</summary>
    public bool ShowEpilogue { get; set; }

    /// <summary>A stopped clock (tests): nothing slides or swings, and torches stop flickering, so pictures repeat.</summary>
    public bool Frozen { get; set; }

    public DungeonSession? Session
    {
        get => _session;
        set
        {
            if (_session is not null)
                _session.Published -= OnPublished;
            _session = value;
            _messages.Clear();
            _slides.Clear();
            _actions.Clear();
            _floats.Clear();
            _corpses.Clear();
            _facing.Clear();
            if (value is not null)
            {
                value.Published += OnPublished;
                _messages.Add((value.Opening, Time));
            }
            TerrainChanged?.Invoke();
            Changed?.Invoke();
        }
    }

    /// <summary>Something happened that the views should show (the level, a door, a death).</summary>
    public event Action? Changed;

    /// <summary>The level's terrain changed (a new level, a door opened or broken, a wall torn down): dress it again.</summary>
    public event Action? TerrainChanged;

    /// <summary>Raised for each event, after the view's own bookkeeping (for the window: stop a journey, a rest).</summary>
    public event Action<IGameEvent>? Happened;

    public IReadOnlyList<(string Text, double At)> Messages => _messages;

    public IReadOnlyList<FloatingText> Floats => _floats;

    public IReadOnlyList<Corpse> Corpses => _corpses;

    /// <summary>Spells in flight or bursting: a bolt's streak, a ball's burst, a breath's cone.</summary>
    public IReadOnlyList<SpellFlash> Flashes => _flashes;

    /// <summary>How long a spell's flash lasts.</summary>
    public const double FlashSeconds = 0.5;

    /// <summary>True while anyone is still sliding into the square they moved to.</summary>
    public bool Sliding => !Frozen && _slides.Values.Any(s => Time - s.At < StepSeconds);

    private void OnPublished(IGameEvent e)
    {
        var now = Time;
        switch (e)
        {
            case MessageEvent m:
                _messages.Add((m.Text, now));
                if (_messages.Count > 200)
                    _messages.RemoveAt(0);
                break;
            case HeroMovedEvent moved:
                _slides[HeroKey] = (moved.From, now);
                break;
            case MonsterMovedEvent moved:
                _slides[moved.Id] = (moved.From, now);
                _facing[moved.Id] = DirectionExtensions.FromOffset(moved.To.X - moved.From.X, moved.To.Y - moved.From.Y);
                break;
            case StrikeEvent strike:
                Strike(strike, now);
                break;
            case SpellEvent spell:
                if (IdAt(spell.From) is { } caster)
                {
                    _actions[caster] = (spell.Kind is Engine.Definitions.MonsterSpellKind.Bolt && spell.Element is null ? "shoot" : "cast", now);
                    _facing[caster] = DirectionExtensions.FromOffset(spell.To.X - spell.From.X, spell.To.Y - spell.From.Y);
                }
                _flashes.Add(new SpellFlash(spell.From, spell.To, spell.Kind, spell.Element, spell.Radius, now));
                break;
            case MonsterDiedEvent died:
                _corpses.Add(new Corpse(died.Id, died.At, died.RaceId, Facing(died.Id), now));
                _slides.Remove(died.Id);
                _actions.Remove(died.Id);
                break;
            case DoorOpenedEvent:
                TerrainChanged?.Invoke();
                break;
            case LevelEnteredEvent:
                TerrainChanged?.Invoke();
                _flashes.Clear();
                _slides.Clear();
                _actions.Clear();
                _floats.Clear();
                _corpses.Clear();
                _facing.Clear();
                break;
        }
        Happened?.Invoke(e);
        Changed?.Invoke();
    }

    private void Strike(StrikeEvent strike, double now)
    {
        var attacker = strike.ByHero ? HeroKey : IdAt(strike.From);
        var target = strike.ByHero ? IdAt(strike.To) : HeroKey;
        if (attacker is { } a)
        {
            _actions[a] = ("swing", now);
            if (a != HeroKey)
                _facing[a] = DirectionExtensions.FromOffset(strike.To.X - strike.From.X, strike.To.Y - strike.From.Y);
        }
        if (target is { } t && strike.Damage > 0)
            _actions[t] = ("hit", now);
        var text = strike.Degree < DegreeOfSuccess.Success ? "miss"
            : strike.Degree == DegreeOfSuccess.CriticalSuccess ? $"{strike.Damage}!" : strike.Damage.ToString();
        _floats.Add(new FloatingText(strike.To, text, !strike.ByHero && strike.Damage > 0, now));
    }

    private int? IdAt(Loc p) => _session?.Level.Monsters.At(p)?.Id;

    /// <summary>The way a monster faces: the way it last moved or struck (south by default).</summary>
    public Direction Facing(int id) => _facing.GetValueOrDefault(id, Direction.South);

    /// <summary>
    /// Where someone is drawn, in map units (the centre of their square, or on the way to it), given where they stand.
    /// </summary>
    public (double X, double Y) DrawnAt(int key, Loc at)
    {
        if (Frozen || !_slides.TryGetValue(key, out var slide))
            return (at.X + 0.5, at.Y + 0.5);
        var t = Math.Clamp((Time - slide.At) / StepSeconds, 0, 1);
        return (slide.From.X + (at.X - slide.From.X) * t + 0.5, slide.From.Y + (at.Y - slide.From.Y) * t + 0.5);
    }

    /// <summary>The animation someone is in and how far into it: a swing or a flinch for a moment, a run while sliding, else standing.</summary>
    public (string Animation, double Seconds) Animation(int key)
    {
        var now = Time;
        if (Frozen)
            return ("stance", now);
        if (_actions.TryGetValue(key, out var action) && now - action.At < ActionSeconds)
            return (action.Animation, now - action.At);
        if (_slides.TryGetValue(key, out var slide) && now - slide.At < StepSeconds)
            return ("run", now - slide.At);
        return ("stance", now);
    }

    /// <summary>Drops floating numbers and bodies whose time is up.</summary>
    public void Prune()
    {
        var now = Time;
        _floats.RemoveAll(f => now - f.At > FloatSeconds);
        _corpses.RemoveAll(c => now - c.At > CorpseSeconds);
        _flashes.RemoveAll(f => now - f.At > FlashSeconds);
    }
}

/// <summary>A number (or "miss") floating up from where a blow landed; <see cref="Hurt"/> when it was the hero hit.</summary>
public sealed record FloatingText(Loc Where, string Text, bool Hurt, double At);

/// <summary>A monster's spell on its way: from the caster toward the hero, of a kind and an element.</summary>
public sealed record SpellFlash(Loc From, Loc To, Engine.Definitions.MonsterSpellKind Kind, string? Element, int Radius, double At);

/// <summary>A monster just slain, falling where it stood.</summary>
public sealed record Corpse(int Id, Loc Where, string RaceId, Direction Facing, double At);
