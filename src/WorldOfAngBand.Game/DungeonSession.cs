using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Generation;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Randomness;
using WorldOfAngBand.Engine.Sight;
using WorldOfAngBand.Engine.Time;
using WorldOfAngBand.Engine.World;

namespace WorldOfAngBand.Game;

/// <summary>
/// One game: the hero, the level they are on and what they remember of it, on AVABand's turn scheduler. Commands come
/// in through <see cref="Perform"/>; what happens goes out through <see cref="Published"/>. Everything follows from the
/// seed and the commands, so a game can be played again exactly.
/// </summary>
public sealed class DungeonSession : ITurnHandler
{
    /// <summary>Feet per level, as Angband counts them.</summary>
    public const int FeetPerLevel = 50;

    private readonly DungeonGenerator _generator;
    private readonly VisionSystem _vision = new();
    private readonly ulong _seed;
    private int _levelsMade;

    public DungeonSession(GameData data, ulong seed)
    {
        Data = data;
        _seed = seed;
        _generator = new DungeonGenerator(data);
        Scheduler.Add(Hero);
        Enter(0, StairArrival.None);
    }

    public GameData Data { get; }

    public Hero Hero { get; } = new();

    public TurnScheduler Scheduler { get; } = new();

    /// <summary>The level the hero is on.</summary>
    public Level Level { get; private set; } = null!;

    /// <summary>What the hero has seen of this level.</summary>
    public KnownMap Known { get; private set; } = null!;

    public int Depth => Level.Depth;

    /// <summary>Game turns since the start (ten to a player turn at normal speed).</summary>
    public long Turn => Scheduler.GameTurn;

    public event Action<IGameEvent>? Published;

    /// <summary>
    /// Carries out a command. True when it took the hero's turn (the world then runs on until the hero's next one);
    /// false when nothing happened, such as walking into a wall, which costs no time, as in Angband.
    /// </summary>
    public bool Perform(GameCommand command)
    {
        var took = command switch
        {
            Walk walk => DoWalk(walk.Direction),
            Hold => true,
            TakeStairs stairs => DoStairs(stairs.Down),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown command."),
        };
        if (!took)
            return false;
        // A level change gives the hero a fresh first move; otherwise the turn is paid for and the world runs on.
        if (command is not TakeStairs)
            Hero.Energy -= EnergyTable.MoveEnergy;
        Scheduler.Advance(this);
        UpdateView();
        return true;
    }

    /// <summary>
    /// The way to <paramref name="target"/> through squares the hero knows can be walked (closed doors included: walking
    /// opens them), shortest first, as AVABand's click-to-travel. Null when there is none.
    /// </summary>
    public IReadOnlyList<Direction>? PathTo(Loc target)
    {
        if (!Level.InBounds(target) || !Known.IsKnown(target) || !Walkable(target) || target == Hero.Position)
            return null;
        var from = new Dictionary<Loc, Loc> { [Hero.Position] = Hero.Position };
        var queue = new Queue<Loc>();
        queue.Enqueue(Hero.Position);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at == target)
                break;
            foreach (var dir in DirectionExtensions.Compass)
            {
                var next = at.Step(dir);
                if (!Level.InBounds(next) || from.ContainsKey(next) || !Known.IsKnown(next) || !Walkable(next))
                    continue;
                from[next] = at;
                queue.Enqueue(next);
            }
        }
        if (!from.ContainsKey(target))
            return null;
        var steps = new List<Direction>();
        for (var at = target; at != Hero.Position; at = from[at])
        {
            var back = from[at];
            steps.Add(DirectionExtensions.FromOffset(at.X - back.X, at.Y - back.Y));
        }
        steps.Reverse();
        return steps;
    }

    private bool Walkable(Loc p) => Level.IsPassable(p) || Level.Has(p, TerrainFlags.DoorClosed);

    bool ITurnHandler.NeedsInput(IActor actor) => actor == Hero;

    int ITurnHandler.TakeTurn(IActor actor) => EnergyTable.MoveEnergy;

    void ITurnHandler.OnWorldTick(long gameTurn)
    {
    }

    private bool DoWalk(Direction direction)
    {
        var to = Hero.Position.Step(direction);
        Hero.Facing = direction;
        if (!Level.InBounds(to))
            return false;
        var terrain = Level.FeatureAt(to);
        if (terrain.Has(TerrainFlags.DoorClosed))
        {
            Level.SetFeature(to, Data.Terrain.Ids.OpenDoor);
            Publish(new DoorOpenedEvent(to));
            return true;
        }
        if (!terrain.Has(TerrainFlags.Passable))
        {
            // Walls the hero has not seen yet are felt in the dark, and remembered, as in Angband.
            if (!Known.IsKnown(to))
                Known.Remember(Level, to);
            Publish(new MessageEvent($"There is {Article(terrain.Name)} in the way."));
            return false;
        }
        var from = Hero.Position;
        Hero.Position = to;
        Publish(new HeroMovedEvent(from, to));
        return true;
    }

    private bool DoStairs(bool down)
    {
        var here = Level.FeatureAt(Hero.Position);
        if (!here.Has(down ? TerrainFlags.DownStair : TerrainFlags.UpStair))
        {
            Publish(new MessageEvent(down ? "I see no down staircase here." : "I see no up staircase here."));
            return false;
        }
        Publish(new MessageEvent(down ? "You enter a maze of down staircases." : "You enter a maze of up staircases."));
        Enter(Depth + (down ? 1 : -1), down ? StairArrival.Descended : StairArrival.Ascended);
        return true;
    }

    /// <summary>Makes the level at <paramref name="depth"/> and puts the hero on it. The town is the same on every visit.</summary>
    private void Enter(int depth, StairArrival arrival)
    {
        var seed = depth == 0
            ? GameRandom.DeriveSeed(_seed, 0)
            : GameRandom.DeriveSeed(_seed, depth, ++_levelsMade);
        var generated = _generator.Generate(new LevelRequest(depth, seed, arrival));
        if (Level is not null)
            _vision.Reset(Level);
        Level = generated.Level;
        Known = new KnownMap(Level.Width, Level.Height);
        if (Level.IsKnown)
        {
            foreach (var p in Level.AllLocs())
                Known.Remember(Level, p);
        }
        Hero.Position = generated.PlayerStart;
        Hero.MaxDepth = Math.Max(Hero.MaxDepth, depth);
        // Angband: the player always gets the first move on a new level.
        Hero.Energy = EnergyTable.MoveEnergy;
        UpdateView();
        Publish(new LevelEnteredEvent(depth));
    }

    private void UpdateView()
    {
        var lights = Hero.LightRadius > 0 ? [new LightSource(Hero.Position, Hero.LightRadius)] : Array.Empty<LightSource>();
        _vision.Update(Level, Known, Hero.Position, Data.Constants.MaxSight, lights);
    }

    private void Publish(IGameEvent e) => Published?.Invoke(e);

    private static string Article(string noun) => noun.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(noun[0])) ? $"an {noun}" : $"a {noun}";
}
