using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Engine.Monsters;
using WorldOfAngBand.Engine.World;

namespace WorldOfAngBand.Game;

/// <summary>
/// Things on the floor: what the generator asked for on a new level (objects, good and great ones, gold), and what
/// monsters drop when they die (Angband monster_drop), each made by AVABand's port of Angband's object factory and put
/// down beside where it fell (Angband drop_near).
/// </summary>
internal sealed class Loot(DungeonSession session)
{
    private Level Level => session.Level;

    /// <summary>Places the objects and gold the level's generator planned.</summary>
    public void Populate()
    {
        foreach (var hint in Level.SpawnHints)
        {
            var depth = Math.Max(1, Level.Depth + hint.DepthBonus);
            var item = hint.Kind switch
            {
                SpawnKind.Gold => session.Factory.MakeGold(session.Rng, depth),
                SpawnKind.Object => session.Factory.Make(session.Rng, depth),
                SpawnKind.GoodObject => session.Factory.Make(session.Rng, depth, good: true),
                SpawnKind.GreatObject => session.Factory.Make(session.Rng, depth, good: true, great: true),
                _ => null,
            };
            if (item is not null && Level.InBounds(hint.Loc))
                Level.Objects.Add(hint.Loc, item);
        }
    }

    /// <summary>
    /// Angband monster_drop: as many objects as its DROP flags give (40%, 60% and 90% chances, then 1 to 4), each gold or an
    /// item as ONLY_GOLD and ONLY_ITEM say (else half and half), at the depth of the monster and the level between them,
    /// good or great as DROP_GOOD and DROP_GREAT say.
    /// </summary>
    public void Drop(Monster monster)
    {
        var race = monster.Race;
        var rng = session.Rng;
        var number = (race.Has("DROP_40") && rng.RandInt0(100) < 40 ? 1 : 0) + (race.Has("DROP_60") && rng.RandInt0(100) < 60 ? 1 : 0)
                     + (race.Has("DROP_90") && rng.RandInt0(100) < 90 ? 1 : 0) + (race.Has("DROP_1") ? 1 : 0) + (race.Has("DROP_2") ? 2 : 0) + (race.Has("DROP_3") ? 3 : 0) + (race.Has("DROP_4") ? 4 : 0);
        var level = Math.Max(1, (race.Depth + Level.Depth) / 2);
        var good = race.Has("DROP_GOOD");
        var great = race.Has("DROP_GREAT");
        for (var i = 0; i < number; i++)
        {
            var gold = race.Has("ONLY_GOLD") || (!race.Has("ONLY_ITEM") && rng.OneIn(2));
            var item = gold ? session.Factory.MakeGold(rng, level) : session.Factory.Make(rng, level, good, great);
            if (item is not null)
                DropNear(monster.Position, item);
        }
    }

    /// <summary>Puts an item down at <paramref name="at"/>, or on the nearest open floor beside it (Angband drop_near).</summary>
    public void DropNear(Loc at, Item item)
    {
        foreach (var ring in Enumerable.Range(0, 4))
        {
            foreach (var p in Around(at, ring))
            {
                if (!Level.InBounds(p) || !Level.Has(p, TerrainFlags.Object) || Level.Objects.At(p).Count >= 8)
                    continue;
                Level.Objects.Add(p, item);
                return;
            }
        }
    }

    private static IEnumerable<Loc> Around(Loc at, int ring)
    {
        if (ring == 0)
        {
            yield return at;
            yield break;
        }
        for (var dy = -ring; dy <= ring; dy++)
        for (var dx = -ring; dx <= ring; dx++)
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring)
                yield return new Loc(at.X + dx, at.Y + dy);
    }
}
