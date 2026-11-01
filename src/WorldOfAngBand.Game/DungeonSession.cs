using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Generation;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Monsters;
using WorldOfAngBand.Engine.Randomness;
using WorldOfAngBand.Engine.Sight;
using WorldOfAngBand.Engine.Time;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Content;
using WorldOfAngBand.Rules.Creatures;

namespace WorldOfAngBand.Game;

/// <summary>
/// One game: the hero, the level they are on and what they remember of it, and the monsters on it, on AVABand's turn
/// scheduler. Commands come in through <see cref="Perform"/>; what happens goes out through <see cref="Published"/>.
/// Everything follows from the seed and the commands, so a game can be played again exactly. Fighting is
/// <see cref="Fight"/>'s and monsters' thinking <see cref="MonsterBrain"/>'s.
/// </summary>
public sealed class DungeonSession : ITurnHandler
{
    /// <summary>Feet per level, as Angband counts them.</summary>
    public const int FeetPerLevel = 50;

    /// <summary>Seconds of the rules' clock per game turn: an action at normal speed (ten game turns) is half a second, as in AshenReliquary.</summary>
    public const double SecondsPerGameTurn = 0.05;

    /// <summary>How long a rest takes to heal the hero fully, in their turns.</summary>
    public const int TurnsToRestFully = 60;

    /// <summary>
    /// A monster is worth this many times Pathfinder's experience: Pathfinder's numbers pace a party through a few
    /// fights a level, Angband's hero through dozens.
    /// </summary>
    public const int ExperienceMultiplier = 2;

    private readonly DungeonGenerator _generator;
    private readonly MonsterSpawner _spawner;
    private readonly VisionSystem _vision = new();
    private readonly Dictionary<int, Foe> _foes = [];
    private readonly HashSet<string> _uniquesGone = [];
    private readonly ulong _seed;
    private readonly MonsterBrain _brain;
    private int _levelsMade;
    private double _restHealing;

    public DungeonSession(GameData data, ulong seed, ContentDatabase? content = null, CreatureBridge? bridge = null)
    {
        Data = data;
        _seed = seed;
        Content = content ?? ContentDatabase.Embedded;
        Bridge = bridge ?? CreatureBridge.Load(data);
        Rng = new GameRandom(GameRandom.DeriveSeed(seed, -1));
        Roller = new RulesRandom(Rng);
        _generator = new DungeonGenerator(data);
        _spawner = new MonsterSpawner(data) { Allowed = Bridge.Has };
        _brain = new MonsterBrain(this);
        Fight = new Fight(this);
        Hero = Hero.Create(Content);
        Scheduler.Add(Hero);
        Enter(0, StairArrival.None);
    }

    public GameData Data { get; }

    public ContentDatabase Content { get; }

    public CreatureBridge Bridge { get; }

    public Hero Hero { get; }

    public TurnScheduler Scheduler { get; } = new();

    /// <summary>The level the hero is on.</summary>
    public Level Level { get; private set; } = null!;

    /// <summary>What the hero has seen of this level.</summary>
    public KnownMap Known { get; private set; } = null!;

    /// <summary>How far the hero's noise has carried (monsters follow it).</summary>
    public NoiseMap Noise { get; } = new();

    /// <summary>The hero's scent trail (monsters with noses follow it).</summary>
    public ScentMap Scent { get; } = new();

    public int Depth => Level.Depth;

    /// <summary>Game turns since the start (ten to a hero's turn at normal speed).</summary>
    public long Turn => Scheduler.GameTurn;

    /// <summary>The rules' clock, in seconds.</summary>
    public double Now => Turn * SecondsPerGameTurn;

    /// <summary>The game's own random numbers (all of them, so the seed decides everything).</summary>
    internal GameRandom Rng { get; }

    internal RulesRandom Roller { get; }

    internal Fight Fight { get; }

    /// <summary>Set when the hero dies: the line in the Order's book. Nothing more can be done.</summary>
    public DeathRecord? Death { get; private set; }

    public bool IsOver => Death is not null;

    /// <summary>The monsters on this level, as fighters.</summary>
    public IEnumerable<Foe> Foes => _foes.Values;

    public Foe? FoeAt(Loc p) => Level.Monsters.At(p) is { } m ? _foes.GetValueOrDefault(m.Id) : null;

    public Foe? FoeFor(Monster monster) => _foes.GetValueOrDefault(monster.Id);

    public event Action<IGameEvent>? Published;

    /// <summary>
    /// Carries out a command. True when it took the hero's turn (the world then runs on until the hero's next one);
    /// false when nothing happened, such as walking into a wall, which costs no time, as in Angband.
    /// </summary>
    public bool Perform(GameCommand command)
    {
        if (IsOver)
            return false;
        var took = command switch
        {
            Walk walk => DoWalk(walk.Direction),
            Hold => true,
            Rest => DoRest(),
            QuaffFlask => DoQuaff(),
            TakeStairs stairs => DoStairs(stairs.Down),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown command."),
        };
        if (!took)
            return false;
        // A level change gives the hero a fresh first move; otherwise the action is paid for and the world runs on.
        if (command is not TakeStairs)
        {
            Hero.Energy -= EnergyTable.MoveEnergy;
            Hero.Round.EndAction();
        }
        RunWorld();
        return true;
    }

    /// <summary>Runs the world until the hero's next turn, or until the hero dies.</summary>
    private void RunWorld()
    {
        UpdateView();
        while (!IsOver && Scheduler.Advance(this, maxGameTurns: 1) is null)
        {
        }
        UpdateView();
    }

    /// <summary>
    /// Why resting would stop at once, or null when the hero can rest: a monster in view, or nothing to heal.
    /// </summary>
    public string? CannotRest()
    {
        if (VisibleFoes().Any())
            return "You cannot rest with monsters nearby!";
        if (Hero.HitPoints >= Hero.MaxHitPoints && !Hero.Character.Conditions.Active.Any() && !Hero.Character.Persistent.Types.Any())
            return "You have no need to rest.";
        return null;
    }

    /// <summary>The monsters the hero can see.</summary>
    public IEnumerable<Foe> VisibleFoes() => _foes.Values.Where(Seen);

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

    // --- The scheduler's turns ---

    bool ITurnHandler.NeedsInput(IActor actor) => actor == Hero && Hero.Character.Conditions.CanAct;

    int ITurnHandler.TakeTurn(IActor actor)
    {
        if (actor == Hero)
        {
            // Stunned (Angband's paralysis): the turn goes by.
            Say("You cannot move!");
            Hero.Round.EndAction();
            return EnergyTable.MoveEnergy;
        }
        return actor is Monster monster && _foes.TryGetValue(monster.Id, out var foe) ? _brain.TakeTurn(foe) : EnergyTable.MoveEnergy;
    }

    /// <summary>Every ten game turns (a hero's turn at normal speed): conditions and persistent damage run on, and monsters heal.</summary>
    void ITurnHandler.OnWorldTick(long gameTurn)
    {
        const double tick = EnergyTable.GameTurnsPerNormalTurn * SecondsPerGameTurn;
        var character = Hero.Character;
        character.Conditions.Update(tick);
        foreach (var (type, amount) in character.Persistent.Update(tick, Roller))
        {
            var dealt = DamageCalculator.ApplyDefenses(amount, type, character.Defenses);
            if (dealt <= 0)
                continue;
            Say($"You take {dealt} {type.ToString().ToLowerInvariant()} damage.");
            HurtHero(dealt, type == DamageType.Poison ? "poison" : type.ToString().ToLowerInvariant());
            if (IsOver)
                return;
        }
        foreach (var foe in _foes.Values.ToList())
        {
            foe.Conditions.Update(tick);
            foreach (var (type, amount) in foe.Persistent.Update(tick, Roller))
            {
                if (!foe.Monster.IsActive || foe.Monster.IsRemoved)
                    break;
                Fight.Hurt(foe, DamageCalculator.ApplyDefenses(amount, type, foe.Defenses));
            }
            // Angband regen_monster: every 100 game turns, a hundredth of its hit points (twice that if it regenerates).
            var m = foe.Monster;
            if (gameTurn % 100 == 0 && m.IsActive && m.Hp < m.MaxHp)
            {
                var gain = m.MaxHp / 100;
                if (gain == 0 && Rng.OneIn(2))
                    gain = 1;
                if (m.Race.Has(MonsterFlags.Regenerate))
                    gain *= 2;
                m.Hp = Math.Min(m.MaxHp, m.Hp + gain);
            }
        }
    }

    // --- The hero's commands ---

    private bool DoWalk(Direction direction)
    {
        var to = Hero.Position.Step(direction);
        Hero.Facing = direction;
        if (!Level.InBounds(to))
            return false;
        if (FoeAt(to) is { } foe)
        {
            Fight.HeroStrikes(foe);
            return true;
        }
        var terrain = Level.FeatureAt(to);
        if (terrain.Has(TerrainFlags.DoorClosed))
        {
            SetDoor(to, Data.Terrain.Ids.OpenDoor);
            return true;
        }
        if (!terrain.Has(TerrainFlags.Passable))
        {
            // Walls the hero has not seen yet are felt in the dark, and remembered, as in Angband.
            if (!Known.IsKnown(to))
                Known.Remember(Level, to);
            Say($"There is {Article(terrain.Name)} in the way.");
            return false;
        }
        var from = Hero.Position;
        Hero.Position = to;
        Publish(new HeroMovedEvent(from, to));
        return true;
    }

    /// <summary>One turn of rest: a full rest (<see cref="TurnsToRestFully"/> turns) heals every hit point.</summary>
    private bool DoRest()
    {
        if (CannotRest() is { } why)
        {
            Say(why);
            return false;
        }
        _restHealing += Hero.MaxHitPoints / (double)TurnsToRestFully;
        var whole = (int)_restHealing;
        _restHealing -= whole;
        Hero.Character.Heal(whole);
        return true;
    }

    /// <summary>A healing flask (AshenReliquary's: three charges, refilled in town): 1d8 + 2 × level.</summary>
    private bool DoQuaff()
    {
        var potion = Hero.Character.Potion;
        if (potion.Drink(Now, Hero.Level, Roller, Hero.Character.Conditions) is not { } healed)
        {
            Say(potion.Charges == 0 ? "Your flasks are empty." : "You can't drink yet.");
            return false;
        }
        Hero.Character.Heal(healed);
        Say($"You drink from a flask and feel better ({healed} HP). {potion.Charges} left.");
        return true;
    }

    private bool DoStairs(bool down)
    {
        var here = Level.FeatureAt(Hero.Position);
        if (!here.Has(down ? TerrainFlags.DownStair : TerrainFlags.UpStair))
        {
            Say(down ? "I see no down staircase here." : "I see no up staircase here.");
            return false;
        }
        Say(down ? "You enter a maze of down staircases." : "You enter a maze of up staircases.");
        Enter(Depth + (down ? 1 : -1), down ? StairArrival.Descended : StairArrival.Ascended);
        return true;
    }

    // --- Levels ---

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
        Noise.Reset(Level);
        Scent.Reset(Level);

        Scheduler.Clear();
        _foes.Clear();
        Scheduler.Add(Hero);
        // Angband: the player always gets the first move on a new level.
        Hero.Energy = EnergyTable.MoveEnergy;
        if (depth == 0)
        {
            // Gallowmere: the flasks are filled again and Second Breath comes back.
            Hero.Character.Potion.Refill();
            Hero.Character.SecondBreath.Refresh();
        }
        else
        {
            _spawner.Populate(Level, Rng, Hero.Position, _uniquesGone);
        }
        foreach (var monster in Level.Monsters.All)
            Enlist(monster);
        UpdateView();
        Publish(new LevelEnteredEvent(depth));
    }

    private Foe Enlist(Monster monster)
    {
        var foe = new Foe(monster, Bridge.StatsFor(monster.Race), CreatureBridge.DefensesFor(monster.Race), Bridge.NameOf(monster.Race));
        _foes[monster.Id] = foe;
        Scheduler.Add(monster);
        return foe;
    }

    /// <summary>A monster born or called up during play (a breeder's young).</summary>
    internal void Spawn(MonsterRaceDef race, Loc at, bool asleep)
    {
        var monster = _spawner.Place(Level, Rng, race, at, asleep);
        Enlist(monster);
        monster.IsVisible = SeenMonster(monster);
    }

    internal void MoveMonster(Monster monster, Loc to)
    {
        var from = monster.Position;
        Level.Monsters.Move(monster, to);
        Publish(new MonsterMovedEvent(monster.Id, from, to));
    }

    internal void SetDoor(Loc at, ushort feature)
    {
        Level.SetFeature(at, feature);
        Publish(new DoorOpenedEvent(at));
    }

    internal void DestroyWall(Loc at)
    {
        ref var square = ref Level[at];
        square.Feature = Data.Terrain.Ids.Floor;
        square.Flags &= ~SquareFlags.AnyWallMarker;
        Publish(new DoorOpenedEvent(at));
    }

    // --- Seeing, fighting and dying ---

    /// <summary>Whether the hero can see a monster: its square lit and in view, or right beside them.</summary>
    internal bool SeenMonster(Monster monster)
    {
        if (Hero.Character.Conditions.Has(ConditionKind.Blinded) || !Level.InBounds(monster.Position))
            return false;
        ref var square = ref Level[monster.Position];
        return square.Has(SquareFlags.Seen) || (square.Has(SquareFlags.View) && monster.Position.ChebyshevTo(Hero.Position) <= 1);
    }

    public bool Seen(Foe foe) => SeenMonster(foe.Monster);

    /// <summary>What to call a monster in a sentence: a unique by name, the rest with "a" or "an".</summary>
    internal string Called(Foe foe) => foe.Monster.Race.IsUnique ? foe.Name : Article(foe.Name);

    internal string NameOf(Monster monster) => _foes.TryGetValue(monster.Id, out var foe) ? foe.Name : Bridge.NameOf(monster.Race);

    /// <summary>Two monsters on opposite sides of the hero: the hero is off-guard to them.</summary>
    internal bool Flanked()
    {
        var hero = Hero.Position;
        foreach (var foe in _foes.Values)
        {
            var p = foe.Monster.Position;
            if (p.ChebyshevTo(hero) != 1)
                continue;
            if (FoeAt(new Loc(2 * hero.X - p.X, 2 * hero.Y - p.Y)) is not null)
                return true;
        }
        return false;
    }

    internal void HurtHero(int damage, string killer)
    {
        switch (Hero.Character.TakeDamage(damage))
        {
            case DeathOutcome.SavedBySecondBreath:
                Say("You catch a Second Breath, and stand!");
                Publish(new SecondBreathEvent());
                break;
            case DeathOutcome.Died:
                Die(killer);
                break;
        }
    }

    private void Die(string killer)
    {
        var c = Hero.Character;
        Death = new DeathRecord(c.Name, c.Ancestry.Name, c.Class.Name, c.Level, Depth, Hero.MaxDepth,
            killer, Turn / EnergyTable.GameTurnsPerNormalTurn,
            _seed, DateTimeOffset.Now);
        Say("You die.");
        Publish(new HeroDiedEvent(Death));
    }

    /// <summary>A monster slain: it is gone, and the hero learns from it (Pathfinder's experience by level).</summary>
    internal void Kill(Foe foe)
    {
        var monster = foe.Monster;
        var seen = Seen(foe);
        monster.Hp = -1;
        Level.Monsters.Remove(monster);
        monster.IsRemoved = true;
        _foes.Remove(monster.Id);
        if (monster.Race.IsUnique)
            _uniquesGone.Add(monster.Race.Id);
        var living = !monster.Race.Has(MonsterFlags.Undead) && !monster.Race.Has("NONLIVING");
        Say(seen ? $"You have {(living ? "slain" : "destroyed")} the {foe.Name}." : "You have killed it.");
        Publish(new MonsterDiedEvent(monster.Id, monster.Position, foe.Name, monster.Race.Id));
        GainExperience(Encounter.CreatureXp(foe.Level, Hero.Level) * ExperienceMultiplier);
    }

    private void GainExperience(int amount)
    {
        Hero.Experience += amount;
        while (Hero.Experience >= Hero.ExperiencePerLevel && Hero.Level < Character.MaxLevel)
        {
            Hero.Experience -= Hero.ExperiencePerLevel;
            var next = Hero.Level + 1;
            // Until there is a level-up screen: the key attribute, Constitution, Dexterity and Wisdom at boost levels.
            Hero.Character.LevelUp(Rules.Rules.AttributeSet.BoostLevels.Contains(next)
                ? [Hero.Character.KeyAttribute, Rules.Rules.AttributeKind.Constitution, Rules.Rules.AttributeKind.Dexterity, Rules.Rules.AttributeKind.Wisdom]
                : null);
            Say($"Welcome to level {Hero.Level}.");
            Publish(new LevelUpEvent(Hero.Level));
        }
    }

    private void UpdateView()
    {
        var lights = Hero.LightRadius > 0 ? [new LightSource(Hero.Position, Hero.LightRadius)] : Array.Empty<LightSource>();
        _vision.Update(Level, Known, Hero.Position, Data.Constants.MaxSight, lights,
            Hero.Character.Conditions.Has(ConditionKind.Blinded));
        Noise.Update(Level, Hero.Position);
        Scent.Lay(Level, Hero.Position);
        foreach (var foe in _foes.Values)
        {
            var visible = Seen(foe);
            if (visible && !foe.Monster.IsVisible)
                Publish(new MonsterAppearedEvent(foe.Monster.Id, foe.Name));
            foe.Monster.IsVisible = visible;
        }
    }

    internal void Say(string text) => Publish(new MessageEvent(text));

    internal void Publish(IGameEvent e) => Published?.Invoke(e);

    private static string Article(string noun) => noun.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(noun[0])) ? $"an {noun}" : $"a {noun}";
}
