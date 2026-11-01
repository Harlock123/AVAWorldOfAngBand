using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game.Province;

namespace WorldOfAngBand.Game.Bots;

/// <summary>
/// A bot that walks a province zone (the spec's M7c): to each of AshenReliquary's packs in turn, nearest first, fighting
/// what it meets as the dive bot does (and drinking, running and resting as it does). It never goes through a gate on
/// purpose.
/// </summary>
public sealed class ProvinceBot
{
    private readonly DungeonSession _game;
    private readonly DiveBot _fighter;
    private readonly List<Loc> _packs;

    public ProvinceBot(DungeonSession game)
    {
        _game = game;
        _fighter = new DiveBot(game) { Wary = true };
        _packs = (game.Zone?.Spawns ?? []).Where(s => s.StandsTenYearsOn).Select(s => ZoneBuilder.LevelAt(s.At)).ToList();
    }

    /// <summary>The packs' places still to go to.</summary>
    public int PacksLeft => _packs.Count;

    public void Step()
    {
        var hero = _game.Hero;
        // A fight: something beside the hero, or something awake that can come to them and they can reach. (A grave
        // that never moves, or an archer across the water, is walked past.)
        var fighting = _game.VisibleFoes().Any(f => f.Monster.Position.ChebyshevTo(hero.Position) <= 1
            || f.Monster.Aware && !f.Monster.Race.Has(Engine.Definitions.MonsterFlags.NeverMove)
               && f.Monster.Position.DistanceTo(hero.Position) <= 8 && _game.PathTo(f.Monster.Position) is not null);
        if (fighting || hero.HitPoints < hero.MaxHitPoints * 0.6)
        {
            _fighter.Step();
            return;
        }
        while (_packs.Count > 0 && _packs.MinBy(p => p.DistanceTo(hero.Position)) is var next && next.DistanceTo(hero.Position) <= 2.5)
            _packs.Remove(next);
        if (_packs.Count == 0)
        {
            _game.Perform(new Hold());
            return;
        }
        var target = _packs.MinBy(p => p.DistanceTo(hero.Position));
        // Not through a gate: a path that would step into one's reach is left for the next pack (leaving one is fine).
        if (_game.PathTo(target) is [var first, ..]
            && !_game.Gates.Values.Any(g => hero.Position.Step(first).DistanceTo(g) <= 2.5 && hero.Position.DistanceTo(g) > 2.5)
            && _game.Perform(new Walk(first)))
            return;
        _packs.Remove(target);
    }
}
