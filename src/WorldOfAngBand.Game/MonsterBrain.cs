using WorldOfAngBand.Engine.Combat;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Monsters;
using WorldOfAngBand.Engine.Time;
using WorldOfAngBand.Engine.World;

namespace WorldOfAngBand.Game;

/// <summary>
/// How monsters think, ported from AVABand's port of Angband 4.2.5's mon-move.c (its spells, decoys, hirelings,
/// mimics and locks left for later). Each turn a monster that can sense the hero wakes by degrees, rouses its group,
/// may breed, then moves: erratically, toward what it senses (sight, then the hero's noise, then their scent), round
/// the hero with its pack, or away when afraid. Moving into the hero is a melee action.
/// </summary>
internal sealed class MonsterBrain(DungeonSession session)
{
    /// <summary>Angband's keypad directions as offsets: 1 south-west … 9 north-east.</summary>
    private static readonly Loc[] Keypad =
        [new(0, 0), new(-1, 1), new(0, 1), new(1, 1), new(-1, 0), new(0, 0), new(1, 0), new(-1, -1), new(0, -1), new(1, -1)];

    /// <summary>Angband ddd: the eight directions in its usual order.</summary>
    private static readonly int[] Ddd = [2, 8, 6, 4, 3, 1, 9, 7];

    /// <summary>Angband side_dirs: a direction, then those either side of it (biased right, then left, from 10 up).</summary>
    private static readonly int[,] SideDirs =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0 }, { 1, 4, 2, 7, 3, 8, 6, 9 }, { 2, 1, 3, 4, 6, 7, 9, 8 }, { 3, 2, 6, 1, 9, 4, 8, 7 },
        { 4, 7, 1, 8, 2, 9, 3, 6 }, { 5, 5, 5, 5, 5, 5, 5, 5 }, { 6, 3, 9, 2, 8, 1, 7, 4 }, { 7, 8, 4, 9, 1, 6, 2, 3 },
        { 8, 9, 7, 6, 4, 3, 1, 2 }, { 9, 6, 8, 3, 7, 2, 4, 1 },
        { 0, 0, 0, 0, 0, 0, 0, 0 }, { 1, 2, 4, 3, 7, 6, 8, 9 }, { 2, 3, 1, 6, 4, 9, 7, 8 }, { 3, 6, 2, 9, 1, 8, 4, 7 },
        { 4, 1, 7, 2, 8, 3, 9, 6 }, { 5, 5, 5, 5, 5, 5, 5, 5 }, { 6, 9, 3, 8, 2, 7, 1, 4 }, { 7, 4, 8, 1, 9, 2, 6, 3 },
        { 8, 7, 9, 4, 6, 1, 3, 2 }, { 9, 8, 6, 7, 3, 4, 2, 1 },
    };

    private Level Level => session.Level;
    private Loc HeroAt => session.Hero.Position;
    private Engine.Randomness.GameRandom Rng => session.Rng;

    /// <summary>One monster's turn (Angband process_monsters for one monster). Returns the energy it spent.</summary>
    public int TakeTurn(Foe foe)
    {
        var monster = foe.Monster;
        if (session.Hero.Character.IsDead || !monster.IsActive)
            return EnergyTable.MoveEnergy;
        if (!CheckActive(monster))
            return EnergyTable.MoveEnergy;
        if (ProcessTimed(monster) || !foe.Conditions.CanAct)
        {
            foe.Round.EndAction();
            return EnergyTable.MoveEnergy;
        }
        GroupRouse(monster);
        if (monster.Race.Has(MonsterFlags.Multiply) && TryMultiply(monster))
            return EnergyTable.MoveEnergy;
        MoveTurn(foe);
        foe.Round.EndAction();
        return EnergyTable.MoveEnergy;
    }

    // --- What a monster senses ---

    /// <summary>Angband monster_can_hear: the hero's noise here is within its hearing, less a third of their stealth.</summary>
    private bool CanHear(Monster monster)
    {
        var noise = session.Noise[monster.Position];
        return noise != NoiseMap.Unreached && monster.Race.Hearing - session.Hero.Stealth / 3 > noise;
    }

    /// <summary>Angband monster_can_see_player: its square is in the hero's view (sight is symmetric).</summary>
    private bool CanSee(Monster monster) => Level[monster.Position].Has(SquareFlags.View);

    /// <summary>Angband monster_can_smell: the scent here is younger than its nose reaches.</summary>
    private bool CanSmell(Monster monster)
    {
        var scent = session.Scent[monster.Position];
        return scent != 0 && monster.Race.Smell > scent;
    }

    private bool HatesGrid(Monster monster, Loc p) => Level.Has(p, TerrainFlags.Fiery) && !monster.Race.Has("IM_FIRE");

    private static bool PassesWalls(Monster monster) =>
        monster.Race.Has(MonsterFlags.PassWall) || monster.Race.Has(MonsterFlags.KillWall);

    /// <summary>
    /// Angband monster_check_active: a monster takes its turn only if it can reach the hero through walls, is hurt,
    /// stands in view, or hears or smells them.
    /// </summary>
    private bool CheckActive(Monster monster)
    {
        monster.Active = (monster.Position.DistanceTo(HeroAt) <= monster.Race.Hearing && PassesWalls(monster))
                         || monster.Hp < monster.MaxHp || Level[monster.Position].Has(SquareFlags.View)
                         || CanHear(monster) || CanSmell(monster);
        return monster.Active;
    }

    /// <summary>
    /// Angband process_monster_timed: asleep, it may stir (and does nothing else); its fear wears off. True when the
    /// turn is lost.
    /// </summary>
    private bool ProcessTimed(Monster monster)
    {
        if (monster.Sleep > 0)
        {
            ReduceSleep(monster);
            return true;
        }
        if (Rng.OneIn(10) && monster.Active)
            monster.Aware = true;
        if (monster.Fear > 0)
        {
            monster.Fear = Math.Max(0, monster.Fear - Rng.RandInt1(monster.Race.Depth / 10 + 1));
            if (monster.Fear == 0 && session.SeenMonster(monster))
                session.Say($"The {session.NameOf(monster)} recovers its courage.");
        }
        if (monster.Held > 0)
        {
            monster.Held--;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Angband monster_reduce_sleep: if a cube of 0…1023 falls within the hero's noise (2^(30 − stealth)), its sleep
    /// drops — by 100 over the noise here when it is close enough to hear, else by 1.
    /// </summary>
    private void ReduceSleep(Monster monster)
    {
        var heroNoise = 1L << Math.Clamp(30 - session.Hero.Stealth, 0, 62);
        long notice = Rng.RandInt0(1024);
        if (notice * notice * notice > heroNoise)
            return;
        var noise = session.Noise[monster.Position];
        var reduction = noise is > 0 and < 50 ? 100 / noise : 1;
        monster.Sleep = Math.Max(0, monster.Sleep - reduction);
        if (monster.Sleep == 0 && session.SeenMonster(monster))
            session.Say($"The {session.NameOf(monster)} wakes up.");
    }

    /// <summary>Angband monster_group_rouse: a monster aware of the hero may wake the sleeping friends it can see.</summary>
    private void GroupRouse(Monster monster)
    {
        if (!monster.Aware)
            return;
        foreach (var friend in Level.Monsters.All.Where(f => f != monster && f.GroupId == monster.GroupId && f.Sleep > 0).ToList())
        {
            if (!ProjectionPath.Projectable(Level, monster.Position, friend.Position, session.Data.Constants.MaxSight))
                continue;
            if (Rng.OneIn(monster.Position.DistanceTo(friend.Position) * 20))
            {
                friend.Sleep = 0;
                friend.Aware = Rng.RandInt0(100) < 50;
            }
        }
    }

    /// <summary>
    /// Angband process_monster_multiply: a breeder fills an empty square beside it, less often the more crowded it is,
    /// up to a level-wide cap.
    /// </summary>
    private bool TryMultiply(Monster monster)
    {
        var breeders = Level.Monsters.All.Count(m => m.Race.Has(MonsterFlags.Multiply));
        if (breeders >= session.Data.Constants.MaxBreeders)
            return false;
        var crowd = Level.Monsters.All.Count(m => m.Position.ChebyshevTo(monster.Position) <= 1);
        if (crowd >= 4 || !Rng.OneIn(crowd * session.Data.Constants.BreedRate))
            return false;
        var spots = Level.Neighbors(monster.Position).Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && p != HeroAt).ToList();
        if (spots.Count == 0)
            return false;
        session.Spawn(monster.Race, Rng.Pick(spots), asleep: false);
        return true;
    }

    private enum Stagger { None, Confused, Innate }

    /// <summary>
    /// Angband monster_turn after its ranged attack: a stagger (RAND_25/RAND_50) or a chosen direction, then up to
    /// five tries (that direction and those either side) at moving, opening or bashing a door, or attacking the hero.
    /// </summary>
    private void MoveTurn(Foe foe)
    {
        var monster = foe.Monster;
        var stagger = ShouldStagger(monster);
        var dir = 0;
        var tracking = false;
        if (stagger == Stagger.None && !GetMove(monster, out dir, out tracking))
            return;

        var didSomething = false;
        for (var i = 0; i < 5 && !didSomething; i++)
        {
            var d = stagger != Stagger.None ? Ddd[Rng.RandInt0(8)] : SideDirs[dir, i];
            var grid = monster.Position + Keypad[d];
            if (i > 0 && stagger == Stagger.None && !Level[monster.Position].Has(SquareFlags.View) && tracking)
                break;
            if (!Level.InBounds(grid) || !CanEnter(monster, grid, ref didSomething))
                continue;
            if (grid == HeroAt)
            {
                if (monster.Race.Has(MonsterFlags.NeverBlow))
                    continue;
                session.Fight.MonsterMelee(foe);
                didSomething = true;
                break;
            }
            if (monster.Race.Has(MonsterFlags.NeverMove))
                return;
            if (Level.Monsters.At(grid) is not null)
                continue;
            session.MoveMonster(monster, grid);
            didSomething = true;
        }
        if (!didSomething && monster.Fear > 0)
        {
            // Angband: cornered and afraid, it freezes.
            var amount = monster.Fear;
            monster.Fear = 0;
            if (!monster.Race.Has("NO_HOLD"))
                monster.Held = Math.Min(50, Math.Max(monster.Held, amount));
        }
    }

    /// <summary>Angband monster_turn_should_stagger: RAND_25 and RAND_50 move it at random that often.</summary>
    private Stagger ShouldStagger(Monster monster)
    {
        var chance = (monster.Race.Has(MonsterFlags.Rand25) ? 25 : 0) + (monster.Race.Has("RAND_50") ? 50 : 0);
        return Rng.RandInt0(100) < chance ? Stagger.Innate : Stagger.None;
    }

    /// <summary>
    /// Angband get_move: toward what it senses, a tracking friend, the last place it tracked to, or at random; a pack
    /// hides from a hero in a corridor and surrounds one in the open; an afraid monster runs. False if it stays put.
    /// </summary>
    private bool GetMove(Monster monster, out int dir, out bool tracking)
    {
        dir = 0;
        var groupAi = monster.Race.Has(MonsterFlags.GroupAi);
        var offset = new Loc(0, 0);
        var done = false;

        if (GetMoveAdvance(monster, out tracking))
        {
            offset = monster.MoveTarget - monster.Position;
            monster.Tracking = true;
        }
        else if (GroupTracker(monster) is { } tracker
                 && ProjectionPath.Projectable(Level, monster.Position, tracker.Position, session.Data.Constants.MaxSight))
        {
            offset = tracker.Position - monster.Position;
            monster.Tracking = false;
        }
        else
        {
            if (monster.Tracking)
                offset = monster.MoveTarget - monster.Position;
            if (offset == new Loc(0, 0))
            {
                offset = GetMoveRandom(monster);
                monster.Tracking = false;
            }
        }

        if (!done && groupAi && !PassesWalls(monster))
        {
            var open = Ddd.Count(d => Level.InBounds(HeroAt + Keypad[d])
                                      && (Level.IsPassable(HeroAt + Keypad[d]) || Level[HeroAt + Keypad[d]].Has(SquareFlags.Room)));
            if (open < 5 && session.Hero.HitPoints > session.Hero.MaxHitPoints / 2 && FindHiding(monster))
            {
                done = true;
                offset = monster.MoveTarget - monster.Position;
                monster.Tracking = false;
            }
        }

        if (!done && monster.IsAfraid)
        {
            if (FindSafety(monster))
            {
                Flee(monster);
                offset = monster.MoveTarget - monster.Position;
            }
            else
            {
                offset = new Loc(-offset.X, -offset.Y);
            }
            monster.Tracking = false;
            done = true;
        }

        if (!done && groupAi && Level[monster.Position].Has(SquareFlags.View))
        {
            // Surround the hero: a free square beside them, from a random start.
            var grid = monster.MoveTarget;
            if (monster.Position.DistanceTo(HeroAt) > 1)
            {
                var start = Rng.RandInt0(8);
                for (var i = 0; i < 8; i++)
                {
                    grid = HeroAt + Keypad[Ddd[(start + i) % 8]];
                    if (IsEmptyForMonster(grid))
                        break;
                }
            }
            offset = grid - monster.Position;
        }

        if (offset == new Loc(0, 0))
            return false;
        dir = ChooseDirection(offset);
        return true;
    }

    private bool IsEmptyForMonster(Loc g) => Level.InBounds(g) && Level.IsEmptyFloor(g) && g != HeroAt;

    private Monster? GroupTracker(Monster monster) =>
        Level.Monsters.All.FirstOrDefault(t => t != monster && t.GroupId == monster.GroupId && t.Tracking && t.Active);

    /// <summary>
    /// Angband get_move_advance: straight at a hero it can pass walls to or see; else to the neighbour where the noise
    /// is loudest (or as loud); else where the scent is freshest. False if it senses nothing.
    /// </summary>
    private bool GetMoveAdvance(Monster monster, out bool track)
    {
        track = false;
        var baseHearing = monster.Race.Hearing - session.Hero.Stealth / 3;
        var currentNoise = baseHearing - session.Noise[monster.Position];
        if (PassesWalls(monster) || CanSee(monster))
        {
            monster.MoveTarget = HeroAt;
            return true;
        }
        Loc? best = null, backup = null;
        if (CanHear(monster))
        {
            foreach (var d in Ddd)
            {
                var grid = monster.Position + Keypad[d];
                if (!Level.InBounds(grid))
                    continue;
                var noise = session.Noise[grid];
                if (noise == NoiseMap.Unreached || Level.Monsters.At(grid) is not null || HatesGrid(monster, grid))
                    continue;
                var heard = baseHearing - noise;
                if (heard > currentNoise)
                {
                    best = grid;
                    break;
                }
                if (heard == currentNoise)
                    backup = grid;
            }
        }
        if (best is null && CanSmell(monster))
        {
            var bestScent = 0;
            foreach (var d in Ddd)
            {
                var grid = monster.Position + Keypad[d];
                if (!Level.InBounds(grid))
                    continue;
                var scent = session.Scent[grid];
                var smelled = monster.Race.Smell - scent;
                if (smelled > bestScent && scent != 0)
                {
                    bestScent = smelled;
                    best = grid;
                }
            }
        }
        if ((best ?? backup) is { } go)
        {
            monster.MoveTarget = go;
            track = true;
            return true;
        }
        return false;
    }

    /// <summary>Angband get_move_random: a random walkable, bearable neighbour (none if there is none).</summary>
    private Loc GetMoveRandom(Monster monster)
    {
        int[] attempts = [0, 1, 2, 3, 4, 5, 6, 7];
        var left = 8;
        while (left > 0)
        {
            var i = Rng.RandInt0(left);
            var step = Keypad[Ddd[attempts[i]]];
            var grid = monster.Position + step;
            if (Level.InBounds(grid) && Level.IsPassable(grid) && Level.Monsters.At(grid) is null && !HatesGrid(monster, grid))
                return step;
            left--;
            (attempts[i], attempts[left]) = (attempts[left], attempts[i]);
        }
        return new Loc(0, 0);
    }

    /// <summary>The squares at each Angband distance from 1 to 9, ring by ring (Angband dist_offsets, in row order).</summary>
    private static readonly Loc[][] Rings = BuildRings();

    private static Loc[][] BuildRings()
    {
        var rings = new List<Loc>[10];
        for (var d = 0; d < 10; d++)
            rings[d] = [];
        for (var y = -9; y <= 9; y++)
        for (var x = -9; x <= 9; x++)
        {
            var d = new Loc(0, 0).DistanceTo(new Loc(x, y));
            if (d is > 0 and < 10)
                rings[d].Add(new Loc(x, y));
        }
        return [.. rings.Select(r => r.ToArray())];
    }

    /// <summary>
    /// Angband get_move_find_safety: the nearest ring holding a passable square out of the hero's view and not much
    /// louder than here; of those, the one furthest from the hero.
    /// </summary>
    private bool FindSafety(Monster monster)
    {
        var noiseHere = session.Noise[monster.Position];
        for (var d = 1; d < 10; d++)
        {
            Loc? best = null;
            var gdis = 0;
            foreach (var off in Rings[d])
            {
                var grid = monster.Position + off;
                if (!Level.InBoundsFully(grid) || !Level.IsPassable(grid) || HatesGrid(monster, grid))
                    continue;
                if (session.Noise[grid] > noiseHere + 2 * d || Level[grid].Has(SquareFlags.View))
                    continue;
                var dis = grid.DistanceTo(HeroAt);
                if (dis > gdis)
                {
                    best = grid;
                    gdis = dis;
                }
            }
            if (gdis > 0 && best is { } go)
            {
                monster.MoveTarget = go;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Angband get_move_find_hiding: the nearest ring holding an empty square out of view, at least three quarters of
    /// its distance from the hero (plus 2); of those, the one nearest the hero.
    /// </summary>
    private bool FindHiding(Monster monster)
    {
        var min = HeroAt.DistanceTo(monster.Position) * 3 / 4 + 2;
        for (var d = 1; d < 10; d++)
        {
            Loc? best = null;
            var gdis = 999;
            foreach (var off in Rings[d])
            {
                var grid = monster.Position + off;
                if (!Level.InBoundsFully(grid) || !IsEmptyForMonster(grid) || Level[grid].Has(SquareFlags.View))
                    continue;
                if (!ProjectionPath.Projectable(Level, monster.Position, grid, session.Data.Constants.MaxSight))
                    continue;
                var dis = grid.DistanceTo(HeroAt);
                if (dis < gdis && dis >= min)
                {
                    best = grid;
                    gdis = dis;
                }
            }
            if (gdis < 999 && best is { } go)
            {
                monster.MoveTarget = go;
                return true;
            }
        }
        return false;
    }

    /// <summary>Angband get_move_flee: the neighbour best balancing nearness to its safe square against the noise.</summary>
    private void Flee(Monster monster)
    {
        if (!CanHear(monster) && !CanSmell(monster) && !CanSee(monster))
            return;
        var best = new Loc(0, 0);
        var bestScore = -1;
        for (var i = 7; i >= 0; i--)
        {
            var grid = monster.Position + Keypad[Ddd[i]];
            if (!Level.InBounds(grid))
                continue;
            var score = Math.Max(0, 5000 / (grid.DistanceTo(monster.MoveTarget) + 3) - 500 / (session.Noise[grid] + 1));
            if (score < bestScore)
                continue;
            bestScore = score;
            best = grid;
        }
        monster.MoveTarget = best;
    }

    /// <summary>Angband get_move_choose_direction: the keypad direction nearest the offset, ties broken by the game turn.</summary>
    private int ChooseDirection(Loc offset)
    {
        int dx = offset.X, dy = offset.Y, ay = Math.Abs(dy), ax = Math.Abs(dx);
        var even = session.Turn % 2 == 0;
        int dir;
        if (ay > ax * 2)
        {
            if (dy > 0) { dir = 2; if (dx > 0 || (dx == 0 && even)) dir += 10; }
            else { dir = 8; if (dx < 0 || (dx == 0 && even)) dir += 10; }
        }
        else if (ax > ay * 2)
        {
            if (dx > 0) { dir = 6; if (dy < 0 || (dy == 0 && even)) dir += 10; }
            else { dir = 4; if (dy > 0 || (dy == 0 && even)) dir += 10; }
        }
        else if (dy > 0)
        {
            if (dx > 0) { dir = 3; if (ay < ax || (ay == ax && even)) dir += 10; }
            else { dir = 1; if (ay > ax || (ay == ax && even)) dir += 10; }
        }
        else
        {
            if (dx > 0) { dir = 9; if (ay > ax || (ay == ax && even)) dir += 10; }
            else { dir = 7; if (ay < ax || (ay == ax && even)) dir += 10; }
        }
        return dir;
    }

    /// <summary>
    /// Angband monster_turn_can_move: the hero's square or open ground; through rock with PASS_WALL; tearing it down
    /// with KILL_WALL; a closed door opened (OPEN_DOOR) or burst open (BASH_DOOR), which uses the turn.
    /// </summary>
    private bool CanEnter(Monster monster, Loc grid, ref bool didSomething)
    {
        if (grid == HeroAt)
            return true;
        if (HatesGrid(monster, grid))
            return false;
        var feature = Level.FeatureAt(grid);
        if (feature.Has(TerrainFlags.Passable))
            return true;
        if (feature.Has(TerrainFlags.Permanent))
            return false;
        if (monster.Race.Has(MonsterFlags.PassWall))
            return true;
        if (monster.Race.Has(MonsterFlags.KillWall))
        {
            session.DestroyWall(grid);
            return true;
        }
        if (!feature.Has(TerrainFlags.DoorClosed) && !feature.Has(TerrainFlags.Secret))
            return false;
        var canOpen = monster.Race.Has(MonsterFlags.OpenDoor);
        var canBash = monster.Race.Has(MonsterFlags.BashDoor);
        if (!canOpen && !canBash)
            return false;
        didSomething = true;
        if (canOpen && !(canBash && Rng.OneIn(2)))
        {
            session.SetDoor(grid, session.Data.Terrain.Ids.OpenDoor);
            return false;
        }
        session.SetDoor(grid, session.Data.Terrain.Ids.BrokenDoor);
        session.Say("You hear a door burst open!");
        return true;
    }
}
