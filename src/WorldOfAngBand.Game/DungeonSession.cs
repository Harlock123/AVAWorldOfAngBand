using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Generation;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Monsters;
using WorldOfAngBand.Engine.Randomness;
using WorldOfAngBand.Engine.Sight;
using WorldOfAngBand.Engine.Time;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Game.Dressing;
using WorldOfAngBand.Game.Province;
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

    /// <summary>The randart set's seed is the game's, salted ("RAND", as AVABand's).</summary>
    public const long RandartSeedSalt = 0x52414E44;

    /// <summary>How long a rest takes to heal the hero fully, in their turns.</summary>
    public const int TurnsToRestFully = 60;

    /// <summary>
    /// A monster is worth this many times Pathfinder's experience: Pathfinder's numbers pace a party through a few
    /// fights a level, Angband's hero through dozens, and the deep's monsters stand a few levels below the hero who
    /// reaches them (CreatureBridge.LevelForDepth against Bots/Campaign.ExpectedLevel).
    /// </summary>
    public const int ExperienceMultiplier = 3;

    private readonly DungeonGenerator _generator;
    private readonly MonsterSpawner _spawner;
    private readonly VisionSystem _vision = new();
    private readonly Dictionary<int, Foe> _foes = [];
    private readonly HashSet<string> _uniquesGone = [];
    private readonly ulong _seed;
    private readonly MonsterBrain _brain;
    private int _levelsMade;
    private double _restHealing;

    public DungeonSession(GameData data, ulong seed, ContentDatabase? content = null, CreatureBridge? bridge = null,
        ItemBridge? itemBridge = null, Birth? birth = null)
    {
        Birth = birth ?? new Birth();
        Setting.UseMods(data.ModDirectories);
        Data = data;
        _seed = seed;
        Content = content ?? ContentDatabase.Embedded;
        Bridge = bridge ?? CreatureBridge.Load(data);
        ItemBridge = itemBridge ?? ItemBridge.Load(data);
        Rng = new GameRandom(GameRandom.DeriveSeed(seed, -1));
        Roller = new RulesRandom(Rng);
        Knowledge = new PlayerKnowledge(data, GameRandom.DeriveSeed(seed, -2));
        Factory = new ObjectFactory(data)
        {
            AllowCurses = false, AllowedKind = ItemBridge.Has, AllowedEgo = ItemBridge.Allows,
        };
        // Artifacts (the spec's section 6.3): the standard set, or with the randarts birth option a new one made for this
        // game; only those on kinds brought into the game.
        var artifacts = Birth.Randarts ? RandartGenerator.Generate(data, GameRandom.DeriveSeed(seed, RandartSeedSalt), Setting.NameWords) : data.Artifacts;
        Factory.Artifacts = artifacts.Where(a => data.Object(a.Kind) is { } kind && ItemBridge.Has(kind)).ToList();
        _generator = new DungeonGenerator(data);
        // AshenReliquary's creatures only come in the packs its zones place.
        _spawner = new MonsterSpawner(data) { Allowed = r => Bridge.Has(r) && !r.Has(AshenCreature.ProvinceFlag) };
        _brain = new MonsterBrain(this);
        Fight = new Fight(this);
        Spells = new MonsterSpells(this);
        Loot = new Loot(this);
        Belongings = new Belongings(this);
        Effects = new ItemEffects(this);
        Hero = Birth.Character is { } choices ? Hero.Create(Content, choices) : Hero.Create(Content);
        Powers = new Powers(this);
        Kit();
        ThinFlasks();
        // The rumour: drawn at birth, unless the player chose it (a Legacy).
        var drawn = Story.Rumours[Rng.RandInt0(Story.Rumours.Count)];
        var rumour = Birth.Rumour is { } chosen && Story.Rumours.FirstOrDefault(r => r.Ending == chosen) is { Ending: not null } picked ? picked : drawn;
        Rumour = rumour.Ending;
        Stores = new Stores(this);
        Scheduler.Add(Hero);
        // Restless Foes: every monster a little quicker.
        if (Birth.Twist == "restless")
            Scheduler.SpeedBonus = actor => actor is Monster ? 2 : 0;
        Enter(0, StairArrival.None);
        Opening = rumour.Rumour;
    }

    /// <summary>What Gallowmere says as a new character sets out: the rumour of how Ashen Reliquary ended.</summary>
    public string Opening { get; }

    /// <summary>Which of Ashen Reliquary's four endings Gallowmere tells this character (destroy, bind, claim, mercy).</summary>
    public string Rumour { get; }

    /// <summary>What the hero has chosen and done in the story, by story level: font, library, oath, nest, pyre, cinder.</summary>
    public IReadOnlyDictionary<string, string> Flags => _flags;

    private readonly Dictionary<string, string> _flags = [];

    /// <summary>The Cinder of Ashur is out.</summary>
    public bool Won => _flags.ContainsKey("cinder");

    /// <summary>The epilogue, once won.</summary>
    public IReadOnlyList<string> Epilogue { get; private set; } = [];

    /// <summary>
    /// A Lantern-sworn's kit, by class: a weapon and armour (a fighter's long sword, shield and mail; a wizard's staff and
    /// robe; and so on), boots and gloves, a torch, two potions of Cure Light Wounds, two scrolls of Phase Door, and a
    /// little gold. All of it known.
    /// </summary>
    private void Kit()
    {
        string[] worn = Hero.Character.Class.Id switch
        {
            "rogue" => ["rapier", "soft_leather_armour"],
            "wizard" => ["quarterstaff", "robe"],
            "priest" => ["mace", "leather_shield", "hard_leather_armour"],
            "druid" => ["quarterstaff", "soft_leather_armour"],
            "blackguard" => ["battle_axe", "chain_mail"],
            _ => ["long_sword", "leather_shield", "chain_mail"],
        };
        foreach (var id in worn.Concat(["leather_boots", "leather_gloves", "wooden_torch"]))
        {
            var item = Factory.Create(id);
            Belongings.LearnAll(item);
            Hero.Pack.Wield(item, () => Factory.NextSerial++);
        }
        foreach (var (id, count) in new[] { ("cure_light_wounds", 2), ("phase_door", 2) })
        {
            var item = Factory.Create(id, count);
            Belongings.LearnAll(item);
            Hero.Pack.Add(item);
        }
        Hero.Gold = 150;
        Belongings.Refit();
        Hero.Resource.Reset();
    }

    /// <summary>What the player chose at birth: the hero, the rumour (a Legacy), the birth options.</summary>
    public Birth Birth { get; }

    /// <summary>The hero's class abilities.</summary>
    public Powers Powers { get; }

    public GameData Data { get; }

    public ContentDatabase Content { get; }

    public CreatureBridge Bridge { get; }

    public ItemBridge ItemBridge { get; }

    /// <summary>What the hero knows of items: kinds, flavours and runes (AVABand's port of Angband 4.2's rune knowledge).</summary>
    public PlayerKnowledge Knowledge { get; }

    /// <summary>Makes the items: AVABand's port of Angband's object factory, kept to the kinds and egos bridged so far.</summary>
    public ObjectFactory Factory { get; }

    /// <summary>Gallowmere's shops and the hero's room at the inn.</summary>
    public Stores Stores { get; }

    internal Loot Loot { get; }

    internal Belongings Belongings { get; }

    internal ItemEffects Effects { get; }

    /// <summary>An item's name as the hero knows it.</summary>
    public string NameOf(Item item) => Belongings.Name(item);

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

    internal MonsterSpells Spells { get; }

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
        var actions = 1;
        var before = Level;
        var took = command switch
        {
            Walk walk => DoWalk(walk.Direction),
            Hold => true,
            Rest => DoRest(),
            QuaffFlask => DoQuaff(),
            TakeStairs stairs => DoStairs(stairs.Down),
            PickUp => Belongings.PickUp(),
            DropItem drop => Belongings.Drop(drop.Item, drop.Count),
            WieldItem wield => Belongings.Wield(wield.Item),
            TakeOffItem takeOff => Belongings.TakeOff(takeOff.Item),
            UseItem use => Effects.Use(use.Item, use.Target),
            ChooseStory choose => Choose(choose.StoryId, choose.Choice),
            UsePower power => (actions = Powers.Use(power.Id, power.Target)) > 0,
            ActivateItem activate => Effects.Activate(activate.Item, activate.Target),
            TakeBlessing blessing => UseShrine(blessing.ShrineId, blessing.Blessing),
            StepThroughRift rift => RiftTravel(rift.Destination),
            ChooseLevel level => ChooseLevelPicks(level.Level, level.Choice),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown command."),
        };
        if (!took)
            return false;
        // A level change gives the hero a fresh first move, and choosing feats takes no time; otherwise each action is
        // paid for and the world runs on.
        if (command is ChooseLevel)
            return true;
        if (command is not TakeStairs && Level == before)
        {
            for (var i = 0; i < actions; i++)
            {
                Hero.Energy -= EnergyTable.MoveEnergy;
                Hero.Round.EndAction();
            }
        }
        RunWorld();
        return true;
    }

    /// <summary>Runs the world until the hero's next turn, or until the hero dies.</summary>
    private void RunWorld()
    {
        UpdateView();
        while (!IsOver && _pendingLevel is null && Scheduler.Advance(this, maxGameTurns: 1) is null)
        {
        }
        if (_pendingLevel is { } next && !IsOver)
        {
            _pendingLevel = null;
            Enter(next.Depth, next.Arrival);
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
    /// opens them; shops only as the destination), shortest first, as AVABand's click-to-travel. Null when there is none.
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
                // A journey goes into a shop only when the shop is where it is going.
                if (next != target && Level.Has(next, TerrainFlags.Shop))
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
        HeroTimers();
        if (IsOver)
            return;
        Powers.Tick(tick);
        if (IsOver)
            return;
        Weather(tick);
        if (IsOver)
            return;
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

    /// <summary>
    /// Once a hero's turn: temporary effects wear off, a regenerating hero heals a little, and Word of Recall and Deep
    /// Descent count down to taking the hero away.
    /// </summary>
    private void HeroTimers()
    {
        var changed = false;
        // Worn artifacts recharge, a turn at a time.
        foreach (var item in Hero.Pack.Equipped.Where(i => i.Timeout > 0))
            item.Timeout--;
        foreach (var effect in Hero.Timed.Keys.ToList())
        {
            if (--Hero.Timed[effect] > 0)
                continue;
            Hero.Timed.Remove(effect);
            changed = true;
            Say(effect switch
            {
                "haste" => "You feel yourself slow down.",
                "heroism" => "The heroism wears off.",
                "berserk" => "You feel less berserk.",
                "bless" => "The prayer has expired.",
                _ when effect.StartsWith("resist:", StringComparison.Ordinal) => $"You feel less resistant to {effect[7..].ToLowerInvariant()}.",
                _ => $"The {effect} wears off.",
            });
        }
        if (changed)
            Belongings.Refit();
        if (Hero.Gear.Regenerate && Turn % 100 == 0 && Hero.HitPoints < Hero.MaxHitPoints)
            Hero.Character.Heal(Math.Max(1, Hero.MaxHitPoints / 20));
        if (Hero.RecallIn > 0 && --Hero.RecallIn == 0)
        {
            if (Depth > 0)
            {
                Say("You feel yourself yanked upwards!");
                Publish(new CueEvent(Cue.Recalled));
                _pendingLevel = (0, StairArrival.None);
            }
            else
            {
                Say("You feel yourself yanked downwards!");
                Publish(new CueEvent(Cue.Recalled));
                _pendingLevel = (Math.Max(1, Hero.MaxDepth), StairArrival.None);
            }
        }
        if (Hero.DescentIn > 0 && --Hero.DescentIn == 0)
        {
            var to = NoDeeperThan(Math.Min(Data.Constants.MaxDepth, Math.Max(Depth, 1) + 5));
            if (Zone is null && to > Depth)
            {
                Say("The floor opens beneath you!");
                Publish(new CueEvent(Cue.Recalled));
                _pendingLevel = (to, StairArrival.None);
            }
            else
            {
                Say("The floor shudders beneath you, and holds.");
            }
        }
    }

    /// <summary>A level change something other than a staircase asked for (recall, descent), made once the world stops.</summary>
    private (int Depth, StairArrival Arrival)? _pendingLevel;

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
        if (Visit(to))
            return true;
        if (Zone is null && Depth == 0 && to == _townRift)
        {
            TouchRift(null);
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
        Belongings.Arrive();
        Altar();
        Wayfare();
        if (terrain.Shop is { } shop && Data.Stores.Any(st => st.Id == shop))
            Publish(new ShopEnteredEvent(shop));
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
        Publish(new CueEvent(Cue.FlaskDrunk));
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
        Publish(new CueEvent(down ? Cue.StairsDown : Cue.StairsUp));
        Enter(Depth + (down ? 1 : -1), down ? StairArrival.Descended : StairArrival.Ascended);
        return true;
    }

    // --- Levels ---

    /// <summary>Takes the hero straight to a depth (tests and, later, the debug menu).</summary>
    internal void JumpTo(int depth)
    {
        Hero.MaxDepth = Math.Max(Hero.MaxDepth, depth);
        Enter(depth, StairArrival.None);
    }

    /// <summary>Makes the level at <paramref name="depth"/> and puts the hero on it. The town is the same on every visit.</summary>
    private void Enter(int depth, StairArrival arrival, string? arriveAtGate = null)
    {
        var seed = depth == 0
            ? GameRandom.DeriveSeed(_seed, 0)
            : GameRandom.DeriveSeed(_seed, depth, ++_levelsMade);
        var story = Story.At(depth);
        // The quest levels (Saint Ysmere's, the Cinder's) have no way down until their keeper is gone.
        var quest = Unfinished(depth);
        var generated = _generator.Generate(new LevelRequest(depth, seed, arrival, Quest: quest,
            QuestRoom: story is not null && (!_flags.ContainsKey(story.Id) || Unfinished(depth)) ? story.Room : null));
        Install(generated.Level, generated.PlayerStart);
        Hero.MaxDepth = Math.Max(Hero.MaxDepth, depth);
        if (depth > 0)
            LevelsDown++;
        if (depth == 0)
        {
            // Gallowmere's gates out to the province (the spec's section 7.4), and its Lantern Rift; back through a gate,
            // the hero is beside it.
            PlaceTownGates();
            PlaceTownRift();
            if (arriveAtGate is not null && _gates.TryGetValue(arriveAtGate, out var gate))
            {
                Hero.Position = Level.Neighbors(gate).Where(Level.IsEmptyFloor).OrderBy(p => p.DistanceTo(new Loc(Level.Width / 2, Level.Height / 2))).FirstOrDefault(gate);
                _insideGate = arriveAtGate;
            }
            // Gallowmere: the flasks are filled again, Second Breath comes back, and the shops have new stock.
            Hero.Character.Potion.Refill();
            ThinFlasks();
            Hero.Character.SecondBreath.Refresh();
            Stores?.Restock();
        }
        else
        {
            // The story's monsters take their places before the level's own. On a quest level the rest of the deep
            // keeps half its distance from the keeper: half the monsters a level that deep would have.
            PlaceStory(story);
            // Swarm: half as many monsters again.
            if (Birth.Twist == "swarm")
                Level.Population = Level.Population with { Monsters = Level.Population.Monsters * 3 / 2 };
            if (quest)
            {
                Level.Population = Level.Population with { Monsters = Level.Population.Monsters / 2 };
                // The shaft comes down by the keeper: the hero arrives just outside its room.
                if (Level.QuestSpots.Where(q => q.Symbol == ')').Select(q => (Loc?)q.Loc).FirstOrDefault() is { } keeper
                    && Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && !Level[p].Has(SquareFlags.Vault)
                                                  && p.ChebyshevTo(keeper) is >= 13 and <= 22)
                        .OrderBy(p => p.DistanceTo(keeper)).Select(p => (Loc?)p).FirstOrDefault() is { } door)
                    Hero.Position = door;
            }
            _spawner.Populate(Level, Rng, Hero.Position, _uniquesGone);
            Loot.Populate();
        }
        // Darkness: nothing in the deep is lit but what glows by itself.
        if (Birth.Twist == "darkness" && depth > 0)
        {
            foreach (var p in Level.AllLocs().Where(p => !Level.FeatureAt(p).Has(TerrainFlags.Bright)))
                Level[p].Flags &= ~SquareFlags.Glow;
        }
        foreach (var monster in Level.Monsters.All)
            Enlist(monster);
        UpdateView();
        Publish(new LevelEnteredEvent(depth));
    }

    /// <summary>
    /// A level comes into play: the old one's sight let go, the new one's memory, noise and scent fresh, its monsters
    /// cleared from the scheduler (uniques left alive can be met again, as in Angband), and the hero at
    /// <paramref name="start"/> with the first move.
    /// </summary>
    private void Install(Level level, Loc start)
    {
        if (Level is not null)
            _vision.Reset(Level);
        Level = level;
        Zone = null;
        _dressed = null;
        _gates = new Dictionary<string, Loc>();
        _insideGate = null;
        Known = new KnownMap(Level.Width, Level.Height);
        if (Level.IsKnown)
        {
            foreach (var p in Level.AllLocs())
                Known.Remember(Level, p);
        }
        Hero.Position = start;
        Noise.Reset(Level);
        Scent.Reset(Level);
        foreach (var foe in _foes.Values.Where(f => f.Monster.Race.IsUnique))
            _uniquesGone.Remove(foe.Monster.Race.Id);
        Scheduler.Clear();
        _foes.Clear();
        Scheduler.Add(Hero);
        // Angband: the player always gets the first move on a new level.
        Hero.Energy = EnergyTable.MoveEnergy;
    }

    // --- The province (the spec's section 7.4) ---

    /// <summary>The province's zones (AshenReliquary's), loaded once.</summary>
    public ProvinceMap Province => ProvinceMap.Shared;

    /// <summary>The province zone the hero is in, or null in Gallowmere and the dungeon.</summary>
    public ZoneDef? Zone { get; private set; }

    private DressedLevel? _dressed;

    /// <summary>
    /// The level in Flare's art: a zone as AshenReliquary drew it (built once), else Angband's level dressed afresh
    /// (LevelDressing), since its doors and veins change.
    /// </summary>
    public DressedLevel Dressed => Zone is not null && _dressed is { } zone ? zone : LevelDressing.Dress(Level);

    /// <summary>The gates on this level (Gallowmere's, or a zone's exits), by exit id.</summary>
    private Dictionary<string, Loc> _gates = [];

    /// <summary>The gate the hero is standing in (passing through one only counts on the way in).</summary>
    private string? _insideGate;

    public IReadOnlyDictionary<string, Loc> Gates => _gates;

    /// <summary>
    /// Gallowmere's two gates, as Fort Gallowmere's two exits: the south gate to the Gallows Road and the west gate to
    /// the Rotwater Fens, each cut through the town's wall where the town's ground comes nearest it.
    /// </summary>
    private void PlaceTownGates()
    {
        var gate = Data.Terrain["zone_gate"].Index;
        var open = Level.AllLocs().Where(Level.IsPassable).ToList();
        if (open.Count == 0)
            return;
        void Cut(string id, Func<Loc, double> nearness, Loc outward)
        {
            foreach (var p in open.OrderBy(nearness))
            {
                var wall = p + outward;
                if (!Level.InBounds(wall) || Level.IsPassable(wall) || Level.FeatureAt(wall).Has(TerrainFlags.Shop))
                    continue;
                Level.SetFeature(wall, gate);
                Level[wall].Flags |= SquareFlags.Glow;
                _gates[id] = wall;
                return;
            }
        }
        var middleX = Level.Width / 2.0;
        var middleY = Level.Height / 2.0;
        foreach (var exit in Province.Gates)
        {
            if (exit.Id == "gate")
                Cut(exit.Id, p => -p.Y * 4 + Math.Abs(p.X - middleX), new Loc(0, 1));
            else
                Cut(exit.Id, p => p.X * 4 + Math.Abs(p.Y - middleY), new Loc(-1, 0));
        }
    }

    /// <summary>
    /// A zone comes into play, built from AshenReliquary's file: the hero at the exit they came through (or the zone's
    /// start), a few of Angband's monsters of the depth the zone stands for wandering it, and its tagline said.
    /// </summary>
    internal void EnterZone(string zoneId, string? arrivingAt)
    {
        var zone = Province[zoneId] ?? throw new InvalidOperationException($"No zone '{zoneId}'.");
        var depth = ProvinceMap.DepthFor(zone.Level);
        var built = ZoneBuilder.Build(zone, Data.Terrain, depth);
        var exit = arrivingAt is null ? null : zone.Exits.FirstOrDefault(e => e.Id == arrivingAt);
        var start = ZoneBuilder.LevelAt(exit?.Arrive ?? exit?.At ?? zone.PlayerStart);
        Install(built.Level, start);
        Zone = zone;
        _dressed = built.Dressed;
        _gates = new Dictionary<string, Loc>(built.Gates);
        if (!Level.IsPassable(Hero.Position))
            Hero.Position = Level.AllLocs().Where(Level.IsEmptyFloor).OrderBy(p => p.DistanceTo(start)).First();
        _insideGate = InsideGate();
        var memory = MemoryOf(zone.Id);
        PlacePacks(zone, memory);
        PlacePlaces(zone, memory, built);
        // A few of Angband's monsters of the zone's depth wander in among AshenReliquary's own.
        var area = zone.Width * zone.Height;
        Level.Population = new PopulationBudget(Monsters: 1 + area / 5000, RoomObjects: 0, AnywhereObjects: 1 + area / 4000, Gold: 2 + area / 3000);
        _spawner.Populate(Level, Rng, Hero.Position, _uniquesGone);
        Loot.Populate();
        foreach (var monster in Level.Monsters.All)
            Enlist(monster);
        UpdateView();
        Publish(new LevelEnteredEvent(depth));
        Say(zone.Tagline is { Length: > 0 } tagline ? $"{zone.Name}. {tagline}" : zone.Name);
    }

    private readonly Dictionary<string, ZoneMemory> _zoneMemory = [];

    /// <summary>How many dungeon levels the hero has gone down into (a zone's dead packs come back after five).</summary>
    public int LevelsDown { get; private set; }

    public ZoneMemory MemoryOf(string zoneId) => _zoneMemory.TryGetValue(zoneId, out var memory) ? memory : _zoneMemory[zoneId] = new ZoneMemory();

    private sealed record AshenPlacement(AshenCreature Creature, int Level, Rules.Creatures.CreatureRank Rank, int Pack);

    /// <summary>The Ashen creatures just placed, by monster id, until they are enlisted.</summary>
    private readonly Dictionary<int, AshenPlacement> _ashenPlaced = [];

    /// <summary>
    /// AshenReliquary's packs where it placed them, asleep at their posts (Ashen's creatures idle until something comes
    /// near): those that stand ten years on, and that the hero hasn't killed lately.
    /// </summary>
    private void PlacePacks(ZoneDef zone, ZoneMemory memory)
    {
        var spawns = zone.Spawns ?? [];
        for (var pack = 0; pack < spawns.Count; pack++)
        {
            var spawn = spawns[pack];
            if (!spawn.StandsTenYearsOn || memory.StillDead(pack, LevelsDown))
                continue;
            memory.SlainAt.Remove(pack);
            var centre = ZoneBuilder.LevelAt(spawn.At);
            foreach (var member in spawn.Pack)
            {
                if (Province.Creature(member.Creature) is not { } creature || Data.Monster(creature.RaceId) is not { } race)
                    continue;
                for (var i = 0; i < member.Count; i++)
                {
                    var spot = Level.AllLocs().Where(p => p.DistanceTo(centre) <= Math.Max(1.5, spawn.Radius + 0.5) && Level.IsEmptyFloor(p) && p != Hero.Position)
                        .OrderBy(_ => Rng.RandInt0(1000)).Select(p => (Loc?)p).FirstOrDefault();
                    if (spot is not { } at)
                        continue;
                    var monster = _spawner.Place(Level, Rng, race, at, asleep: true);
                    monster.Sleep = 8 + Rng.RandInt0(12);
                    _ashenPlaced[monster.Id] = new AshenPlacement(creature, spawn.Level ?? zone.Level, member.Rank, pack);
                }
            }
        }
    }

    /// <summary>Places one of AshenReliquary's creatures, awake, at a level (tests).</summary>
    internal Foe PlaceAshen(string creatureId, Loc at, int level, Rules.Creatures.CreatureRank rank = Rules.Creatures.CreatureRank.Normal)
    {
        var creature = Province.Creature(creatureId) ?? throw new InvalidOperationException($"No creature '{creatureId}'.");
        var monster = _spawner.Place(Level, Rng, Data.Monster(creature.RaceId)!, at, asleep: false);
        _ashenPlaced[monster.Id] = new AshenPlacement(creature, level, rank, -1);
        return Enlist(monster);
    }

    /// <summary>A pack's last creature died: the pack stays dead a while.</summary>
    private void PackSlain(Foe foe)
    {
        if (Zone is not { } zone || foe.Ashen is not { } ashen)
            return;
        if (_foes.Values.Any(f => f != foe && f.Ashen?.Pack == ashen.Pack && f.Monster.IsActive && !f.Monster.IsRemoved))
            return;
        MemoryOf(zone.Id).SlainAt[ashen.Pack] = LevelsDown;
        if (!_foes.Values.Any(f => f != foe && f.Ashen is not null && f.Monster.IsActive && !f.Monster.IsRemoved))
            Publish(new CueEvent(Cue.ZoneCleared));
    }

    /// <summary>
    /// The places in a zone: its caches (Angband's objects of the zone's depth, once each), its shrines (a blessing
    /// from three, once each), and a waystation's trader (the Smith in Ironhold, the Apothecary at Saint Oriel's Rest,
    /// the Quartermaster at Ember Gate).
    /// </summary>
    private void PlacePlaces(ZoneDef zone, ZoneMemory memory, ZoneBuilder.Built built)
    {
        _places.Clear();
        foreach (var cache in zone.Caches ?? [])
        {
            var at = ZoneBuilder.LevelAt(cache.At);
            if (!Level.InBounds(at))
                continue;
            Level.SetFeature(at, Data.Terrain["zone_cache"].Index);
            _places[at] = cache;
            built.Dressed.SetObjects(at, [ZoneBuilder.CachePiece(zone, cache, memory.CachesOpened.Contains(cache.Id))]);
        }
        foreach (var shrine in zone.Shrines ?? [])
        {
            var at = ZoneBuilder.LevelAt(shrine.At);
            if (!Level.InBounds(at))
                continue;
            // A waystation's shrine is a Lantern Rift; the wild's are Ashur's waystones.
            Level.SetFeature(at, Data.Terrain[zone.Town ? "zone_rift" : memory.ShrinesUsed.Contains(shrine.Id) ? "zone_shrine_spent" : "zone_shrine"].Index);
            _places[at] = shrine;
        }
        // The people who stayed, where AshenReliquary put them, and the traders at their stalls.
        _people.Clear();
        foreach (var npc in zone.Npcs ?? [])
        {
            var at = ZoneBuilder.LevelAt(npc.At);
            if (!Level.InBounds(at) || Province.LineOf(zone.Id, npc.Id) is null)
                continue;
            Level.SetFeature(at, Data.Terrain["zone_npc"].Index);
            _places[at] = npc;
            _people.Add(new Person(at, npc.Name ?? "Someone", npc.Look ?? new Dictionary<string, string>(), npc.Facing));
        }
        foreach (var trader in zone.Vendors ?? [])
        {
            var at = ZoneBuilder.LevelAt(trader.At);
            if (Level.InBounds(at) && WaystationTrader(zone.Id) is not null && trader == zone.Vendors![0])
                _people.Add(new Person(at, trader.Name ?? "A trader", trader.Look ?? new Dictionary<string, string>(), trader.Facing));
        }
        // What can be read: notes, graves, pages, keepsakes.
        foreach (var thing in zone.Interactables ?? [])
        {
            var at = ZoneBuilder.LevelAt(thing.At);
            if (!Level.InBounds(at) || !Province.Stands(thing)
                || (Province.Reading(thing.Dialogue) is null && Province.TieInOf(zone.Id, thing.Id) is null))
                continue;
            Level.SetFeature(at, Data.Terrain["zone_lore"].Index);
            _places[at] = thing;
        }
        if (WaystationTrader(zone.Id) is { } store && zone.Vendors is [var vendor, ..])
        {
            var at = ZoneBuilder.LevelAt(vendor.At);
            if (Level.InBounds(at) && Data.Terrain.Shops.FirstOrDefault(t => t.Shop == store) is { } shop)
                Level.SetFeature(at, shop.Index);
        }
    }

    /// <summary>Someone standing in a zone: where, who, in what, facing which way (AshenReliquary's 0 to 7).</summary>
    public sealed record Person(Loc At, string Name, IReadOnlyDictionary<string, string> Look, int Facing);

    private readonly List<Person> _people = [];

    /// <summary>The people standing in this zone (the view draws them).</summary>
    public IReadOnlyList<Person> People => Zone is null ? [] : _people;

    /// <summary>One thing the hero has read or kept: what, where, and what it said.</summary>
    public sealed record JournalEntry(string Title, string Where, string Text);

    private readonly List<JournalEntry> _journal = [];

    /// <summary>The hero's journal: the notes, graves and keepsakes of the province they have read, in order.</summary>
    public IReadOnlyList<JournalEntry> Journal => _journal;

    /// <summary>The one trader at each waystation.</summary>
    public static string? WaystationTrader(string zoneId) => zoneId switch
    {
        "ironhold" => "weaponsmith",
        "saint_oriels_rest" => "alchemist",
        "ember_gate" => "general",
        _ => null,
    };

    /// <summary>A zone's caches and shrines, by the square they stand on.</summary>
    private readonly Dictionary<Loc, ZonePlace> _places = [];

    /// <summary>The hero walked into a cache or a shrine: it opens, or offers its blessings. True when something happened.</summary>
    private bool Visit(Loc at)
    {
        if (Zone is not { } zone || !_places.TryGetValue(at, out var place))
            return false;
        var memory = MemoryOf(zone.Id);
        var terrain = Level.FeatureAt(at).Id;
        if (terrain == "zone_cache")
        {
            if (!memory.CachesOpened.Add(place.Id))
            {
                Say("It is empty.");
                return true;
            }
            var depth = Level.Depth;
            var found = 0;
            var spots = Level.Neighbors(at).Where(Level.IsEmptyFloor).ToList();
            for (var i = 0; i < 2 + Rng.RandInt0(2) && spots.Count > 0; i++)
            {
                if (Factory.Make(Rng, depth + 3, good: true) is not { } item)
                    continue;
                Level.Objects.Add(spots[i % spots.Count], item);
                found++;
            }
            if (spots.Count > 0)
                Level.Objects.Add(spots[^1], Factory.MakeGold(Rng, depth + 3));
            if (_dressed is { } dressed)
                dressed.SetObjects(at, [ZoneBuilder.CachePiece(zone, place, opened: true)]);
            Say(found > 0 ? "You open the cache. Things spill out round it." : "You open the cache: a little gold.");
            Publish(new CueEvent(Cue.CacheOpened, at));
            Publish(new DoorOpenedEvent(at));
            UpdateView();
            return true;
        }
        if (terrain == "zone_shrine")
        {
            Publish(new ShrineEvent(place.Id, place.Name ?? "A shrine", Shrines.Offer(Rng)));
            return true;
        }
        if (terrain == "zone_shrine_spent")
        {
            Say("The shrine is cold. Ashur has given what it will here.");
            return true;
        }
        if (terrain == "zone_npc" && Province.LineOf(zone.Id, place.Id) is { } line)
        {
            Say(place.Still ? line : $"{place.Name}{(string.IsNullOrEmpty(place.Title) ? "" : $", {place.Title}")}: {line}");
            return true;
        }
        if (terrain == "zone_lore")
        {
            Read(zone, place);
            return true;
        }
        if (terrain == "zone_rift")
        {
            TouchRift(zone.Id);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The hero reads something left in the province: it goes into the journal, and a tie-in sets its flag for the story
    /// below (the spec's section 7.4: Sallow's hearth, Agnete's hymnal, Edith Hadric's grave, the cold egg).
    /// </summary>
    private void Read(ZoneDef zone, ZonePlace place)
    {
        var tieIn = Province.TieInOf(zone.Id, place.Id);
        var text = tieIn is { } t && place.Action == "egg" ? t.Text
            : string.Join(" ", new[] { Province.Reading(place.Dialogue), tieIn?.Text }.OfType<string>());
        var title = place.Name ?? "Something written";
        if (_journal.All(e => e.Title != title || e.Where != zone.Name))
            _journal.Add(new JournalEntry(title, zone.Name, text));
        if (tieIn is { } carried && _flags.TryAdd(TieInFlag(carried.Flag), carried.Value))
            Say("You will want that, further down.");
        Publish(new ReadingEvent(title, zone.Name, text));
    }

    /// <summary>The story flag a tie-in sets ("tie.mire").</summary>
    public static string TieInFlag(string flag) => "tie." + flag;

    // --- Lantern Rifts ---

    private readonly HashSet<string> _rifts = [];

    /// <summary>The waystations whose rift the hero has touched.</summary>
    public IReadOnlySet<string> Rifts => _rifts;

    /// <summary>Gallowmere's own rift, by the town's middle.</summary>
    private void PlaceTownRift()
    {
        var middle = new Loc(Level.Width / 2, Level.Height / 2);
        if (Level.AllLocs().Where(Level.IsEmptyFloor).OrderBy(p => p.DistanceTo(middle)).Select(p => (Loc?)p).FirstOrDefault() is { } at)
        {
            Level.SetFeature(at, Data.Terrain["zone_rift"].Index);
            Level[at].Flags |= SquareFlags.Glow;
            _townRift = at;
        }
    }

    private Loc? _townRift;

    /// <summary>A rift touched: a waystation's is learned; then the hero may step through to any other they know.</summary>
    private void TouchRift(string? waystation)
    {
        if (waystation is not null && _rifts.Add(waystation))
            Say($"The rift knows you now. From Gallowmere you can step through to {Province[waystation]?.Name}.");
        var destinations = new List<string>();
        if (waystation is not null)
            destinations.Add(ProvinceMap.Gallowmere);
        destinations.AddRange(_rifts.Where(r => r != waystation).Order());
        if (destinations.Count == 0)
        {
            Say("The rift flickers. You know no other to step through to; touch a waystation's first.");
            return;
        }
        Publish(new RiftEvent(destinations.Select(d => (d, d == ProvinceMap.Gallowmere ? "Gallowmere" : Province[d]?.Name ?? d)).ToList()));
    }

    /// <summary>Steps through a rift to Gallowmere or a known waystation. Takes the hero's turn.</summary>
    private bool RiftTravel(string destination)
    {
        var here = Zone is { Town: true } zone ? zone.Id : Zone is null && Depth == 0 ? ProvinceMap.Gallowmere : null;
        if (here is null || destination == here)
            return false;
        if (destination == ProvinceMap.Gallowmere)
        {
            Enter(0, StairArrival.None);
            if (_townRift is { } rift)
                Hero.Position = Level.Neighbors(rift).Where(Level.IsEmptyFloor).FirstOrDefault(rift);
            Say("You step out of the rift into Gallowmere.");
            Publish(new CueEvent(Cue.RiftStepped));
            return true;
        }
        if (!_rifts.Contains(destination) || Province[destination]?.Shrines is not [var shrine, ..])
            return false;
        EnterZone(destination, null);
        var at = ZoneBuilder.LevelAt(shrine.At);
        Hero.Position = Level.Neighbors(at).Where(Level.IsEmptyFloor).OrderBy(p => p.DistanceTo(Hero.Position)).FirstOrDefault(Hero.Position);
        _insideGate = InsideGate();
        UpdateView();
        Say($"You step out of the rift at {Province[destination]!.Name}.");
        Publish(new CueEvent(Cue.RiftStepped));
        return true;
    }

    /// <summary>Takes a shrine's blessing (one of those it offered). Takes the hero's turn.</summary>
    private bool UseShrine(string shrineId, string blessing)
    {
        if (Zone is not { } zone || zone.Shrines?.FirstOrDefault(s => s.Id == shrineId) is not { } shrine)
            return false;
        var memory = MemoryOf(zone.Id);
        if (!memory.ShrinesUsed.Add(shrineId))
            return false;
        Shrines.Bless(this, blessing);
        Level.SetFeature(ZoneBuilder.LevelAt(shrine.At), Data.Terrain["zone_shrine_spent"].Index);
        Publish(new CueEvent(Cue.BlessingTaken, ZoneBuilder.LevelAt(shrine.At)));
        return true;
    }

    // --- Weather (the spec's section 7.4) ---

    private double _weatherClock;

    /// <summary>An ash storm is blowing (the Ash Wastes): sight is halved while it lasts.</summary>
    public bool Storming { get; private set; }

    /// <summary>How far the hero can see: Angband's sight, halved in an ash storm.</summary>
    public int SightRadius => Storming ? Data.Constants.MaxSight / 2 : Data.Constants.MaxSight;

    /// <summary>
    /// A zone's weather as time passes: ash storms rise for 25 seconds in every minute on the Ash Wastes, and on the
    /// Frostbitten Pass the cold calls for a Fortitude save each minute (DC 16 + the zone's level) or 1d6 cold (twice on a
    /// critical failure; resistance to cold takes it off).
    /// </summary>
    private void Weather(double seconds)
    {
        if (Zone is not { } zone)
        {
            _weatherClock = 0;
            Storming = false;
            return;
        }
        var before = _weatherClock;
        _weatherClock += seconds;
        if (zone.AshStorms)
        {
            var storming = _weatherClock % 60 >= 35;
            if (storming != Storming)
            {
                Storming = storming;
                Say(storming ? "An ash storm rolls over you. You can barely see." : "The ash storm passes.");
                UpdateView();
            }
        }
        if (zone.Cold && (int)(_weatherClock / 60) > (int)(before / 60))
        {
            var c = Hero.Character;
            var check = Rules.Rules.Check.Roll(Roller, c.Save(Rules.Rules.StatisticKind.Fortitude).Total, 16 + zone.Level);
            if (check.Degree >= Rules.Rules.DegreeOfSuccess.Success)
                return;
            var rolled = new Rules.Rules.Dice(check.Degree == Rules.Rules.DegreeOfSuccess.CriticalFailure ? 2 : 1, 6).Roll(Roller);
            var dealt = DamageCalculator.ApplyDefenses(rolled, DamageType.Cold, c.Defenses);
            Say(dealt > 0 ? $"The cold bites: {dealt} cold." : "The cold bites, but your ward holds it off.");
            if (dealt > 0)
                HurtHero(dealt, "the cold of the Pass");
        }
    }

    /// <summary>The gate whose reach the hero stands in, if any.</summary>
    private string? InsideGate()
    {
        foreach (var (id, at) in _gates)
        {
            var radius = Zone?.Exits.FirstOrDefault(e => e.Id == id)?.Radius ?? 1.0;
            if (Hero.Position.DistanceTo(at) <= Math.Max(1.0, radius))
                return id;
        }
        return null;
    }

    /// <summary>The hero walked: into a gate's reach, they go through it (once, until they step out and back in).</summary>
    private void Wayfare()
    {
        var inside = InsideGate();
        if (inside is null || inside == _insideGate)
        {
            _insideGate = inside;
            return;
        }
        _insideGate = inside;
        var exit = Zone is { } zone ? zone.Exits.First(e => e.Id == inside) : Province[ProvinceMap.Gallowmere]!.Exits.First(e => e.Id == inside);
        switch (exit.To)
        {
            case ProvinceMap.Gallowmere:
                Enter(0, StairArrival.None, exit.ToExit);
                Say("You come back through Gallowmere's gate.");
                break;
            case { } to when Province.IsOpen(to):
                EnterZone(to, exit.ToExit);
                break;
            default:
                Say($"The way to {exit.Label} is not open yet.");
                break;
        }
    }

    private Foe Enlist(Monster monster)
    {
        // One of AshenReliquary's, in a zone's pack: its numbers at the pack's level and rank.
        if (_ashenPlaced.Remove(monster.Id, out var placed))
        {
            var (ashen, stats, hp) = AshenFoe.For(placed.Creature, placed.Level, placed.Rank, placed.Pack);
            monster.MaxHp = monster.Hp = hp;
            monster.Speed = placed.Creature.Speed;
            var made = new Foe(monster, stats, placed.Creature.Defenses(), Bridge.NameOf(monster.Race)) { Ashen = ashen };
            _foes[monster.Id] = made;
            Scheduler.Add(monster);
            return made;
        }
        // Pathfinder's hit points for its level, nudged by Angband's as rolled (CreatureBridge.HitPointsFor).
        var wounded = monster.MaxHp > 0 ? (double)monster.Hp / monster.MaxHp : 1;
        monster.MaxHp = Bridge.HitPointsFor(monster.Race, monster.MaxHp);
        monster.Hp = Math.Max(1, (int)Math.Round(monster.MaxHp * wounded));
        monster.Speed = CreatureBridge.SpeedFor(monster.Speed);
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
        Publish(new CueEvent(feature == Data.Terrain.Ids.BrokenDoor ? Cue.DoorBroken : Cue.DoorOpened, at));
    }

    internal void DestroyWall(Loc at)
    {
        ref var square = ref Level[at];
        square.Feature = Data.Terrain.Ids.Floor;
        square.Flags &= ~SquareFlags.AnyWallMarker;
        Publish(new DoorOpenedEvent(at));
    }

    // --- The story ---

    /// <summary>
    /// A story level's room: its altar where the room's '(' is, its monster (with any escort) where ')' is, a good thing
    /// where '[' is. At depth 98, the echo of the Herald the rumour names, if it still stands.
    /// </summary>
    private void PlaceStory(StoryLevel? story)
    {
        foreach (var (symbol, at) in Level.QuestSpots)
        {
            switch (symbol)
            {
                case '(' when story is not null:
                    Level.SetFeature(at, Data.Terrain["quest_altar"].Index);
                    break;
                // The story's monster, with its friends; a keeper (Saint Ysmere, the Cinder) keeps its vigil alone.
                case ')' when story?.Monster is { } id && !_uniquesGone.Contains(id) && Data.Monster(id) is { } race:
                    var placed = race.Has(MonsterFlags.Questor)
                        ? [_spawner.Place(Level, Rng, race, at, asleep: true)]
                        : _spawner.PlaceWithFriends(Level, Rng, race, at, _uniquesGone, asleep: true);
                    _uniquesGone.Add(race.Id);
                    foreach (var m in placed)
                        m.Sleep = Math.Max(m.Sleep, 40);
                    break;
                case '[':
                    if (Factory.Make(Rng, Depth + 5, good: true) is { } item)
                        Level.Objects.Add(at, item);
                    break;
            }
        }
        // (Placed directly: Angband's spawner never picks a quest monster.)
        if (Depth == Story.EchoDepth && Story.Rumours.First(r => r.Ending == Rumour).Echo is var echo
            && !_uniquesGone.Contains(echo) && Data.Monster(echo) is { } herald)
        {
            var spots = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p.DistanceTo(Hero.Position) > 20 && !Level[p].Has(SquareFlags.Vault)).ToList();
            if (spots.Count > 0)
                _spawner.PlaceWithFriends(Level, Rng, herald, Rng.Pick(spots), _uniquesGone, asleep: false);
        }
    }

    /// <summary>The hero walked onto a story level's altar: if its choice is still to be made, they are offered it.</summary>
    private void Altar()
    {
        if (Story.At(Depth) is not { } story || _flags.ContainsKey(story.Id) || Level.FeatureAt(Hero.Position).Id != "quest_altar")
            return;
        var choices = Story.Choices(story.Id, _flags, StoryMonster(story) is not null);
        if (choices.Count > 0)
            Publish(new StoryChoiceEvent(story.Id, story.Title, story.Prompt, choices));
    }

    private Foe? StoryMonster(StoryLevel story) => _foes.Values.FirstOrDefault(f => f.Monster.Race.Id == story.Monster);

    /// <summary>Makes a story choice: its flag is set, and it does what it says. Takes the hero's turn.</summary>
    private bool Choose(string id, int index)
    {
        if (Story.At(Depth) is not { } story || story.Id != id || _flags.ContainsKey(id))
            return false;
        var choices = Story.Choices(id, _flags, StoryMonster(story) is not null);
        if (index < 0 || index >= choices.Count)
            return false;
        var value = choices[index].Value;
        _flags[id] = value;
        var monster = StoryMonster(story);
        switch (id, value)
        {
            case ("font", "sealed"):
                Hero.Boons.Add("resist:Poison");
                Say("The wax hisses and holds. The grey water stops. (You resist poison.)");
                break;
            case ("font", "named"):
                if (monster is not null)
                {
                    Say($"You say the name from Sallow's hearth. {Subject(monster.Monster)} stops, and remembers it, and runs away into the water like rain.");
                    Banish(monster);
                    _uniquesGone.Add(monster.Monster.Race.Id);
                }
                Hero.Boons.Add("resist:Poison");
                Say("The font runs clear. (You resist poison.)");
                break;
            case ("font", "taken"):
                if (Hero.Pack.Weapon is { } weapon && weapon.Brands.All(b => b.Element != "pois"))
                {
                    weapon.Brands.Add(new BrandDef { Element = "pois", Multiplier = 3, Verb = "poison", Name = "poison" });
                    Belongings.Learn(RuneIds.Brand("pois"));
                }
                Say("Your blade drinks the grey water, and stays wet with it.");
                break;
            case ("library", "read"):
                Hero.Boons.Add("conf");
                Say("The first Order did not burn Ashur once. They burned him every year, and Ysmere was the one who lit the pyre, "
                    + "and they never told her what she was burning. (Your mind is clearer for knowing.)");
                break;
            case ("library", "sung"):
                Hero.Boons.Add("conf");
                Hero.Boons.Add("fear");
                Say("At Agnete's hymn the drowned pages rise to the surface, one by one, and you read them dry. The first Order "
                    + "burned Ashur every year, and Ysmere lit the pyre, and never knew. (Your mind is clearer, and fear can't find you.)");
                break;
            case ("library", "left"):
                Say("The pages sink. Whatever they said is the water's now.");
                break;
            case ("oath", "freed"):
                if (monster is not null)
                {
                    Say($"You strike Ser Hadric's name from the stone. {Subject(monster.Monster)} lowers his sword, and is gone.");
                    Banish(monster);
                    _uniquesGone.Add(monster.Monster.Race.Id);
                }
                Hero.Boons.Add("hold_life");
                Say("(Your life holds fast against draining.)");
                break;
            case ("oath", "released"):
                if (monster is not null)
                {
                    Say($"You speak the words from Edith Hadric's grave. {Subject(monster.Monster)} kneels, and is ash, and so, somewhere below, are the Nine.");
                    Banish(monster);
                    _uniquesGone.Add(monster.Monster.Race.Id);
                }
                // The Nine Unburied are released with him: none of them is met again.
                foreach (var race in Data.Monsters.Where(m => m.IsUnique && Bridge.NameOf(m).Contains("of the Nine", StringComparison.Ordinal)))
                    _uniquesGone.Add(race.Id);
                Hero.Boons.Add("hold_life");
                Say("(Your life holds fast against draining.)");
                break;
            case ("oath", _):
                if (monster is not null)
                {
                    monster.Monster.Sleep = 0;
                    monster.Monster.Aware = true;
                    Say($"{Subject(monster.Monster)} draws his sword.");
                }
                else
                {
                    Say("The oath-stone cracks in two.");
                }
                break;
            case ("nest", "spared"):
                if (monster is not null)
                {
                    Say($"{Subject(monster.Monster)} takes the egg in her jaws and goes, up the shaft and away.");
                    Banish(monster);
                    _uniquesGone.Add(monster.Monster.Race.Id);
                }
                break;
            case ("nest", "mourned"):
                if (monster is not null)
                {
                    Say($"{Subject(monster.Monster)} lowers her head to the shard of her first egg, a long time. Then she takes it, and goes home.");
                    Banish(monster);
                    _uniquesGone.Add(monster.Monster.Race.Id);
                }
                Hero.Boons.Add("resist:Fire");
                Say("(The heat of the deep touches you less.)");
                break;
            case ("nest", "smashed"):
                Say("The egg breaks. Something in the dark screams.");
                if (monster is not null)
                {
                    monster.Monster.Sleep = 0;
                    monster.Monster.Aware = true;
                }
                break;
            case ("pyre", "spared"):
                if (monster is not null)
                {
                    Say("Saint Ysmere listens. Then she puts down the torch she has held for nine hundred years, and lowers the last seal.");
                    var at = monster.Monster.Position;
                    Banish(monster);
                    _uniquesGone.Add(monster.Monster.Race.Id);
                    OpenTheWayDown(at);
                }
                break;
            case ("pyre", _):
                if (monster is not null)
                {
                    monster.Monster.Sleep = 0;
                    monster.Monster.Aware = true;
                    Say("Saint Ysmere turns from the pyre.");
                }
                break;
        }
        // The Cinderwyrm's dam, let go, has her own quarrel with Saint Ysmere: she is waiting for her.
        if (id == "pyre" && value == "fought" && _flags.GetValueOrDefault("nest") == "spared" && monster is not null)
        {
            monster.Monster.Hp = monster.Monster.MaxHp * 7 / 10;
            Say("A shadow falls across the pyre: the Cinderwyrm's dam, come for Saint Ysmere. Her breath burns the saint before she flies.");
        }
        Belongings.Refit();
        return true;
    }

    /// <summary>A staircase down where the quest's keeper stood (Angband: the quest's stairs appear).</summary>
    private void OpenTheWayDown(Loc at)
    {
        var spot = Level.IsEmptyFloor(at) ? at : Level.Neighbors(at).FirstOrDefault(Level.IsEmptyFloor);
        if (spot == default)
            return;
        Level.SetFeature(spot, Data.Terrain.Ids.DownStair);
        Known.Remember(Level, spot);
        Say("A staircase down opens in the floor.");
        Publish(new DoorOpenedEvent(spot));
    }

    /// <summary>A story monster or quest keeper slain.</summary>
    private void StoryKill(Monster monster)
    {
        var id = monster.Race.Id;
        if (Story.Levels.FirstOrDefault(l => l.Monster == id) is { } story && !_flags.ContainsKey(story.Id)
            && story.Id is "oath" or "nest" or "font")
            _flags.TryAdd($"{story.Id}.slain", "yes");
        if (id == Story.Ysmere)
        {
            _flags.TryAdd("pyre", "slain");
            _flags["pyre.slain"] = "yes";
            OpenTheWayDown(monster.Position);
        }
        if (id == Story.Cinder)
            Win();
    }

    /// <summary>The Cinder of Ashur is out: the epilogue, and the hero's line in the book (not struck through).</summary>
    private void Win()
    {
        _flags["cinder"] = "out";
        var c = Hero.Character;
        Epilogue = Story.Epilogue(Rumour, _flags, c.Ancestry.Name, c.Class.Name);
        Say("The Cinder of Ashur goes out.");
        Publish(new HeroWonEvent(Epilogue));
    }

    /// <summary>The winner's line in the Order's book, when they lay down the lantern.</summary>
    public DeathRecord Retire()
    {
        var c = Hero.Character;
        return new DeathRecord(c.Name, c.Ancestry.Name, c.Class.Name, c.Level, Depth, Hero.MaxDepth, "", Turn / EnergyTable.GameTurnsPerNormalTurn,
            _seed, DateTimeOffset.Now, Won: true);
    }

    // --- What potions and scrolls do to the level ---

    /// <summary>Thin Flasks: two healing flasks, not three.</summary>
    private void ThinFlasks()
    {
        if (Birth.Twist == "flasks")
            Hero.Character.Potion.Restore(Math.Min(Hero.Character.Potion.Charges, HealingPotion.MaxCharges - 1));
    }

    /// <summary>Glass Cannon: blows struck either way land half again as hard (all but the killing ones).</summary>
    internal int Twisted(int damage) => Birth.Twist == "glass" && damage < 9000 ? damage + damage / 2 : damage;

    /// <summary>The hero moves to a square without walking there (a charge, a tumble, a blink).</summary>
    internal void MoveHero(Loc to)
    {
        var from = Hero.Position;
        Hero.Position = to;
        Publish(new HeroMovedEvent(from, to));
        Belongings.Arrive();
    }

    /// <summary>The hero heals (an ability, a drain), never past their maximum.</summary>
    internal void HealHero(int amount)
    {
        if (amount > 0)
            Hero.Character.Heal(amount);
    }

    /// <summary>
    /// A level's picks (feats, a skill increase, boosts; the spec's section 5.2), made when the player chooses them.
    /// False when they are not allowed.
    /// </summary>
    private bool ChooseLevelPicks(int level, Rules.Characters.LevelChoice choice)
    {
        try
        {
            Hero.Character.Choose(level, choice);
        }
        catch (Rules.Rules.RulesException e)
        {
            Say(e.Message);
            return false;
        }
        Belongings.Refit();
        Publish(new ItemsChangedEvent());
        return true;
    }

    /// <summary>Angband teleport_player: to a random open, empty square at most <paramref name="range"/> away (and not right beside).</summary>
    internal void Teleport(int range)
    {
        var spots = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p != Hero.Position
                                               && p.DistanceTo(Hero.Position) <= range && p.DistanceTo(Hero.Position) > range / 3).ToList();
        if (spots.Count == 0)
            return;
        var from = Hero.Position;
        Hero.Position = Rng.Pick(spots);
        Say("You feel yourself yanked across the dungeon!");
        Publish(new CueEvent(Cue.Teleported));
        Publish(new HeroMovedEvent(from, Hero.Position));
        Publish(new LevelEnteredEvent(Depth));
    }

    /// <summary>Angband light_room: the squares round the hero, and the whole room they stand in, lit for good.</summary>
    internal void LightArea(Loc at, int radius)
    {
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(at) <= radius))
            Level[p].Flags |= SquareFlags.Glow;
        if (Level[at].Has(SquareFlags.Room))
        {
            var seen = new HashSet<Loc> { at };
            var queue = new Queue<Loc>([at]);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                Level[p].Flags |= SquareFlags.Glow;
                if (!Level[p].Has(SquareFlags.Room) || !Level.IsPassable(p))
                    continue;
                foreach (var n in Level.Neighbors(p))
                    if (seen.Add(n))
                        queue.Enqueue(n);
            }
        }
        Publish(new DoorOpenedEvent(at));
    }

    internal void DarkenArea(Loc at, int radius)
    {
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(at) <= radius))
            Level[p].Flags &= ~SquareFlags.Glow;
        Publish(new DoorOpenedEvent(at));
    }

    /// <summary>Angband map_area: every wall, door and staircase within the range that borders open ground is remembered.</summary>
    internal void MapArea(int range)
    {
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(Hero.Position) <= range))
        {
            if (Level.IsPassable(p) && !Level.Has(p, TerrainFlags.Interesting))
                continue;
            if (Level.Neighbors(p).Any(n => Level.IsPassable(n)) || Level.Has(p, TerrainFlags.Interesting))
                Known.Remember(Level, p);
        }
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(Hero.Position) <= range && Level.IsPassable(p)))
            Known.Remember(Level, p);
    }

    /// <summary>Angband detect_gold and sense_objects: what lies on the floor nearby is marked.</summary>
    internal void DetectObjects(int range)
    {
        var found = 0;
        foreach (var (p, item) in Level.Objects.All.Where(o => o.Loc.DistanceTo(Hero.Position) <= range))
        {
            Known.Remember(Level, p);
            Known.RememberObject(p, item);
            found++;
        }
        Say(found > 0 ? "You sense the presence of treasure!" : "You sense no treasure.");
    }

    /// <summary>Angband teleport_level: up or down a level (only down from the town).</summary>
    internal void TeleportLevel()
    {
        // Above ground there is no level up or down to go to.
        if (Zone is not null)
        {
            Say("The ground holds you.");
            return;
        }
        // Never down past a quest level whose keeper still stands.
        var up = Depth > 0 && (Rng.OneIn(2) || Unfinished(Depth));
        Say(up ? "You rise up through the ceiling." : "You sink through the floor.");
        Publish(new CueEvent(Cue.LevelTeleported));
        _pendingLevel = (Depth + (up ? -1 : 1), StairArrival.None);
    }

    /// <summary>A quest level whose keeper still stands (Saint Ysmere's before the pyre's choice, the Cinder's before the win).</summary>
    private bool Unfinished(int depth) =>
        depth == Story.YsmereDepth && !(_flags.ContainsKey("pyre.slain") || _flags.GetValueOrDefault("pyre") == "spared")
        || depth == Story.CinderDepth && !Won;

    /// <summary>
    /// How far down a level change toward <paramref name="depth"/> may go: no further than the first unfinished quest
    /// level below the hero (Angband's dungeon_get_next_level), and nowhere down from one.
    /// </summary>
    private int NoDeeperThan(int depth)
    {
        for (var d = Depth; d < depth; d++)
        {
            if (Unfinished(d))
                return d;
        }
        return depth;
    }

    /// <summary>Wakes every monster on the level and hastens none (Angband's aggravation, without the haste).</summary>
    internal void Aggravate()
    {
        foreach (var foe in _foes.Values)
        {
            foe.Monster.Sleep = 0;
            foe.Monster.Aware = true;
        }
    }

    /// <summary>Calls up monsters of the depth round a square.</summary>
    internal void SummonNear(Loc at, int count) => SummonNear(at, count, Math.Max(1, Depth), null);

    /// <summary>Calls up to <paramref name="count"/> monsters of <paramref name="depth"/> (of a kind, if asked) round a square; returns how many came.</summary>
    internal int SummonNear(Loc at, int count, int depth, Func<MonsterRaceDef, bool>? kind)
    {
        var placed = 0;
        foreach (var p in Level.Neighbors(at).Concat(Level.AllLocs().Where(q => q.ChebyshevTo(at) == 2)).OrderBy(_ => Rng.RandInt0(100)))
        {
            if (placed >= count || !Level.IsEmptyFloor(p) || p == Hero.Position)
                continue;
            if (_spawner.PickRace(Rng, depth, _uniquesGone, kind) is not { } race)
                break;
            Spawn(race, p, asleep: false);
            if (race.IsUnique)
                _uniquesGone.Add(race.Id);
            placed++;
        }
        if (placed > 0)
            Say(placed == 1 ? "You hear something appear nearby!" : "You hear many things appear nearby!");
        return placed;
    }

    /// <summary>A monster blinks or teleports: to an open square within the range, away from where it was.</summary>
    internal void TeleportMonster(Monster monster, int range)
    {
        var spots = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p != Hero.Position && p.DistanceTo(monster.Position) <= range
                                               && p.DistanceTo(monster.Position) > range / 3).ToList();
        if (spots.Count == 0)
            return;
        var seen = SeenMonster(monster);
        MoveMonster(monster, Rng.Pick(spots));
        if (seen)
            Say($"{Subject(monster)} blinks away.");
    }

    /// <summary>A monster steps through space to beside a square.</summary>
    internal void MoveMonsterNear(Monster monster, Loc at)
    {
        var spot = Level.Neighbors(at).Where(p => Level.IsEmptyFloor(p) && p != Hero.Position).OrderBy(p => p.DistanceTo(monster.Position)).FirstOrDefault();
        if (spot == default)
            return;
        MoveMonster(monster, spot);
        if (SeenMonster(monster))
            Say($"{Subject(monster)} appears beside you!");
    }

    /// <summary>The hero is pulled to beside a square (Angband TELE_TO).</summary>
    internal void PullHeroTo(Loc at)
    {
        var spot = Level.Neighbors(at).Where(Level.IsEmptyFloor).OrderBy(p => p.DistanceTo(Hero.Position)).FirstOrDefault();
        if (spot == default)
            return;
        var from = Hero.Position;
        Hero.Position = spot;
        Say("You are commanded to return.");
        Publish(new CueEvent(Cue.Teleported));
        Publish(new HeroMovedEvent(from, spot));
        Publish(new LevelEnteredEvent(Depth));
    }

    /// <summary>Angband's amnesia: the hero forgets the level, all but what they can see.</summary>
    internal void Forget()
    {
        foreach (var p in Level.AllLocs())
            if (!Level[p].Has(SquareFlags.View))
                Known.Forget(p);
        Say("Your memories fade away.");
        Publish(new DoorOpenedEvent(Hero.Position));
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

    /// <summary>A monster at the start of a sentence: a unique by name, the rest as "The kobold".</summary>
    internal string Subject(Monster monster) => monster.Race.IsUnique
        ? char.ToUpperInvariant(NameOf(monster)[0]) + NameOf(monster)[1..]
        : $"The {NameOf(monster)}";

    /// <summary>A monster after a verb: a unique by name, the rest as "the kobold".</summary>
    internal string Object(Monster monster) => monster.Race.IsUnique ? NameOf(monster) : $"the {NameOf(monster)}";

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
        damage = Twisted(damage);
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
            _seed, DateTimeOffset.Now, Place: Zone?.Name);
        Say("You die.");
        Publish(new HeroDiedEvent(Death));
    }

    /// <summary>A monster sent away: gone from the level with nothing learned and nothing dropped (Angband's banishment).</summary>
    internal void Banish(Foe foe)
    {
        Level.Monsters.Remove(foe.Monster);
        foe.Monster.IsRemoved = true;
        _foes.Remove(foe.Monster.Id);
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
        Say(seen ? $"You have {(living ? "slain" : "destroyed")} {Object(monster)}." : "You have killed it.");
        Publish(new MonsterDiedEvent(monster.Id, monster.Position, foe.Name, monster.Race.Id));
        Loot.Drop(monster);
        StoryKill(monster);
        PackSlain(foe);
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
        // Past level 20, the hero rises through the mythic tiers instead.
        while (Hero.Experience >= Hero.ExperiencePerLevel && Hero.Level >= Character.MaxLevel && Hero.MythicTier < Hero.MaxMythicTier)
        {
            Hero.Experience -= Hero.ExperiencePerLevel;
            Hero.MythicTier++;
            Belongings.Refit();
            Hero.Character.Heal(10);
            Say($"You rise to mythic tier {Hero.MythicTier}.");
            Publish(new MythicTierEvent(Hero.MythicTier));
        }
        if (Hero.Level >= Character.MaxLevel && Hero.MythicTier >= Hero.MaxMythicTier)
            Hero.Experience = Math.Min(Hero.Experience, Hero.ExperiencePerLevel - 1);
    }

    private void UpdateView()
    {
        var lights = Hero.LightRadius > 0 ? [new LightSource(Hero.Position, Hero.LightRadius)] : Array.Empty<LightSource>();
        _vision.Update(Level, Known, Hero.Position, SightRadius, lights,
            Hero.Character.Conditions.Has(ConditionKind.Blinded));
        foreach (var p in _vision.Viewed)
            if (Level[p].Has(SquareFlags.Seen))
                Known.RememberObject(p, Level.Objects.At(p).FirstOrDefault());
        Noise.Update(Level, Hero.Position);
        Scent.Lay(Level, Hero.Position);
        foreach (var foe in _foes.Values)
        {
            var visible = Seen(foe);
            if (visible && !foe.Monster.IsVisible)
                Publish(new MonsterAppearedEvent(foe.Monster.Id, foe.Name, foe.Monster.Race.Id));
            foe.Monster.IsVisible = visible;
        }
    }

    internal void Say(string text) => Publish(new MessageEvent(text));

    internal void Publish(IGameEvent e) => Published?.Invoke(e);

    private static string Article(string noun) => noun.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(noun[0])) ? $"an {noun}" : $"a {noun}";
}
