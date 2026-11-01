using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;

namespace WorldOfAngBand.Game.Bots;

/// <summary>
/// A bot that dives: it fights what it sees, drinks when badly hurt (its flasks, then potions it knows heal), escapes
/// when it has nothing to drink, rests when
/// alone, explores (picking up gold on the way), and goes down the first staircase it finds once it has looked around a
/// little. The balance tests use it to measure how deep a plain fighter gets (the spec's M2: a bot reaches depth 5).
/// </summary>
public sealed class DiveBot(DungeonSession game)
{
    /// <summary>
    /// On a level with no way down (Saint Ysmere's, the Cinder's), go and find its keeper rather than look for stairs;
    /// never go back up. The finale's bot hunts.
    /// </summary>
    public bool Hunt { get; init; }

    /// <summary>
    /// Plays as a diver does: drinks at half its hit points rather than 40%, runs at a third, and heads for the stairs
    /// down (or the keeper, when hunting) instead of fighting what isn't beside it. The balance bots play this way.
    /// </summary>
    public bool Wary { get; init; }

    /// <summary>How many turns it waits for a monster it has seen to come to it.</summary>
    public const int Patience = 5;

    private int _waited;

    /// <summary>How many squares of a level it wants to have seen before it takes the stairs down.</summary>
    public int ExploreBeforeDescending { get; init; } = 250;

    /// <summary>Plays until the hero dies, reaches <paramref name="depth"/>, or <paramref name="maxTurns"/> commands pass.</summary>
    public void PlayTo(int depth, int maxTurns = 40_000)
    {
        for (var i = 0; i < maxTurns && !game.IsOver && game.Depth < depth; i++)
            Step();
    }

    /// <summary>One command.</summary>
    public void Step()
    {
        var hero = game.Hero;
        var foes = game.VisibleFoes().ToList();
        if (foes.Count == 0)
            _waited = 0;
        var hurt = hero.HitPoints < hero.MaxHitPoints * (Wary ? 0.5 : 0.4);
        if (hurt && hero.Character.Potion.Charges > 0 && game.Perform(new QuaffFlask()))
            return;
        // Then a potion it knows heals.
        var healing = hero.Pack.Pack.FirstOrDefault(i => game.Knowledge.IsAware(i.Kind)
            && game.ItemBridge.UseOf(i.Kind)?.StartsWith("heal:", StringComparison.Ordinal) == true);
        if (hurt && healing is not null && game.Perform(new UseItem(healing)))
            return;
        // Badly hurt with monsters about and nothing to drink: the stairs underfoot, else a scroll of Teleportation,
        // else Phase Door.
        if (hero.HitPoints < hero.MaxHitPoints * (Wary ? 0.35 : 0.25) && foes.Count > 0 && healing is null)
        {
            if (game.Level.FeatureAt(hero.Position).Has(TerrainFlags.DownStair) && game.Perform(new TakeStairs(Down: true)))
                return;
            if ((hero.Pack.Pack.FirstOrDefault(i => i.Kind.Id == "teleportation") ?? hero.Pack.Pack.FirstOrDefault(i => i.Kind.Id == "phase_door")) is { } escape
                && game.Perform(new UseItem(escape)))
                return;
        }
        // Things that never move (molds, jellies, floating eyes) are left alone: walk on by. Monsters that haven't noticed
        // the hero are left alone unless they are right beside them.
        foes.RemoveAll(f => f.Monster.Race.Has(MonsterFlags.NeverMove) || (!f.Monster.Aware && f.Monster.Position.ChebyshevTo(hero.Position) > 1));
        if (foes.Count > 0)
        {
            // The nearest, but a hunter strikes the keeper whenever it stands beside them.
            var nearest = (Hunt ? foes.FirstOrDefault(f => f.Monster.Race.Has(MonsterFlags.Questor) && f.Monster.Position.ChebyshevTo(hero.Position) == 1) : null)
                          ?? foes.MinBy(f => f.Monster.Position.DistanceTo(hero.Position))!;
            var at = nearest.Monster.Position;
            if (at.ChebyshevTo(hero.Position) == 1)
            {
                game.Perform(new Walk(DirectionExtensions.FromOffset(at.X - hero.Position.X, at.Y - hero.Position.Y)));
                return;
            }
            // A wary bot goes on to where it is going (the stairs down, or the keeper) rather than fight what isn't in
            // its way.
            if (Wary && Goal() is { } goal && Toward(goal))
                return;
            // Wait a few turns for it to come (better a corridor than a room full of them), then go to it: a pack
            // that keeps its distance would otherwise wait forever.
            if (_waited < Patience)
            {
                _waited++;
                game.Perform(new Hold());
                return;
            }
            if (Toward(at))
                return;
        }
        if (hero.HitPoints < hero.MaxHitPoints * 0.7 && game.CannotRest() is null)
        {
            game.Perform(new Rest());
            return;
        }
        // A quest level: go to the keeper (the bot knows where the monsters are, as a player with telepathy would).
        if (Hunt && !game.Level.FindFeature(TerrainFlags.DownStair).Any())
        {
            var keeper = Keeper()
                         ?? game.Foes.OrderBy(f => f.Monster.Position.DistanceTo(hero.Position)).FirstOrDefault();
            if (keeper is not null)
            {
                game.Known.Remember(game.Level, keeper.Monster.Position);
                if (Toward(keeper.Monster.Position) || Explore())
                    return;
            }
        }
        // Look around a little first (unless there is nothing left to see), then take the nearest way down.
        var lookedAround = game.Depth == 0 || game.Level.AllLocs().Count(game.Known.IsKnown) >= ExploreBeforeDescending;
        if (!lookedAround && Explore())
            return;
        if (game.Level.FeatureAt(hero.Position).Has(TerrainFlags.DownStair))
        {
            game.Perform(new TakeStairs(Down: true));
            return;
        }
        if (game.Level.FindFeature(TerrainFlags.DownStair).Where(game.Known.IsKnown).OrderBy(s => s.DistanceTo(hero.Position)).Any(Toward))
            return;
        if (Explore())
            return;
        // No way down to be found: any staircase makes a new level (but a hunter stays).
        if (!Hunt && game.Level.FeatureAt(hero.Position).Has(TerrainFlags.UpStair))
        {
            game.Perform(new TakeStairs(Down: false));
            return;
        }
        var any = game.Level.FindFeature(TerrainFlags.Stair).Where(game.Known.IsKnown).OrderBy(s => s.DistanceTo(hero.Position));
        if (!any.Any(Toward))
            game.Perform(new Hold());
    }

    /// <summary>Where a wary bot is going: a quest level's keeper when hunting, else the nearest known stairs down.</summary>
    private Loc? Goal()
    {
        var hero = game.Hero;
        if (Hunt && !game.Level.FindFeature(TerrainFlags.DownStair).Any())
            return Keeper()?.Monster.Position;
        return game.Level.FindFeature(TerrainFlags.DownStair).Where(game.Known.IsKnown).OrderBy(s => s.DistanceTo(hero.Position))
            .Select(s => (Loc?)s).FirstOrDefault();
    }

    /// <summary>A quest level's keeper (Saint Ysmere, the Cinder), else the nearest unique.</summary>
    private Foe? Keeper() =>
        game.Foes.Where(f => f.Monster.Race.Has(MonsterFlags.Questor)).FirstOrDefault()
        ?? game.Foes.Where(f => f.Monster.Race.IsUnique).OrderBy(f => f.Monster.Position.DistanceTo(game.Hero.Position)).FirstOrDefault();

    /// <summary>A step toward a square the hero knows; false when there is no way.</summary>
    private bool Toward(Loc target)
    {
        if (game.PathTo(target) is not [var first, ..])
            return false;
        return game.Perform(new Walk(first));
    }

    /// <summary>
    /// A step toward the nearest known square beside the unknown, found by one breadth-first search outward from the
    /// hero over what they know can be walked; false when nothing is left to explore.
    /// </summary>
    private bool Explore()
    {
        var level = game.Level;
        var known = game.Known;
        var start = game.Hero.Position;
        var first = new Dictionary<Loc, Direction> { [start] = Direction.None };
        var queue = new Queue<Loc>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at != start && DirectionExtensions.Compass.Any(d => level.InBounds(at.Step(d)) && !known.IsKnown(at.Step(d))))
                return game.Perform(new Walk(first[at]));
            foreach (var d in DirectionExtensions.Compass)
            {
                var next = at.Step(d);
                if (!level.InBounds(next) || first.ContainsKey(next) || !known.IsKnown(next)
                    || !(level.IsPassable(next) || level.Has(next, TerrainFlags.DoorClosed)))
                    continue;
                first[next] = at == start ? d : first[at];
                queue.Enqueue(next);
            }
        }
        return false;
    }
}
