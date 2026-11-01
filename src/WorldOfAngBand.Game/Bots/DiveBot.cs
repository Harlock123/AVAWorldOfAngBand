using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;

namespace WorldOfAngBand.Game.Bots;

/// <summary>
/// A bot that dives: it fights what it sees, drinks when badly hurt, rests when alone, explores, and goes down the
/// first staircase it finds once it has looked around a little. The balance tests use it to measure how deep a plain
/// fighter gets (the spec's M2: a bot reaches depth 5).
/// </summary>
public sealed class DiveBot(DungeonSession game)
{
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
        var hurt = hero.HitPoints < hero.MaxHitPoints * 0.4;
        if (hurt && hero.Character.Potion.Charges > 0 && game.Perform(new QuaffFlask()))
            return;
        // Things that never move (molds, floating eyes) and monsters that haven't noticed the hero are left alone
        // unless they are right beside them.
        foes.RemoveAll(f => (f.Monster.Race.Has(MonsterFlags.NeverMove) || !f.Monster.Aware) && f.Monster.Position.ChebyshevTo(hero.Position) > 1);
        if (foes.Count > 0)
        {
            var nearest = foes.MinBy(f => f.Monster.Position.DistanceTo(hero.Position))!;
            var at = nearest.Monster.Position;
            if (at.ChebyshevTo(hero.Position) == 1)
            {
                game.Perform(new Walk(DirectionExtensions.FromOffset(at.X - hero.Position.X, at.Y - hero.Position.Y)));
                return;
            }
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
        // No way down to be found: any staircase makes a new level.
        if (game.Level.FeatureAt(hero.Position).Has(TerrainFlags.UpStair))
        {
            game.Perform(new TakeStairs(Down: false));
            return;
        }
        var any = game.Level.FindFeature(TerrainFlags.Stair).Where(game.Known.IsKnown).OrderBy(s => s.DistanceTo(hero.Position));
        if (!any.Any(Toward))
            game.Perform(new Hold());
    }

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
