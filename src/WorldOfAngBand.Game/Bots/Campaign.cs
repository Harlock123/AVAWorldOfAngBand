using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Game.Bridge;

namespace WorldOfAngBand.Game.Bots;

/// <summary>How one bot game went: how deep it started and got, whether it lived, and whether it won.</summary>
public sealed record CampaignResult(int From, int Reached, bool Died, bool Won, string? Killer);

/// <summary>
/// The balance bots (the spec's M5: the bots clear it). A hero is made ready for a depth as the spec's section 4.3
/// expects them to be by then: their level, Pathfinder's runes for that level (potency at 2, 10 and 16, striking at 4,
/// 12 and 19, armour potency at 5, 11 and 18), the protections Angband heroes have found by then, healing potions and
/// escape scrolls, and mythic tiers at the bottom. Then the dive
/// bot plays: a few levels on from there, or, at the bottom, Saint Ysmere and the Cinder of Ashur.
/// </summary>
public static class Campaign
{
    /// <summary>The level the spec expects a hero to be at a depth: 1 at the top, 5 by 10, 10 by 30, 16 by 60, 20 by 90.</summary>
    public static int ExpectedLevel(int depth)
    {
        (int Depth, int Level)[] curve = [(1, 1), (10, 5), (30, 10), (60, 16), (90, 20)];
        if (depth <= 1)
            return 1;
        for (var i = 1; i < curve.Length; i++)
        {
            if (depth > curve[i].Depth)
                continue;
            var (d0, l0) = curve[i - 1];
            var (d1, l1) = curve[i];
            return l0 + (l1 - l0) * (depth - d0) / (d1 - d0);
        }
        return 20;
    }

    /// <summary>Makes the hero ready for a depth: level, mythic tiers past 90, runes for their level, and potions.</summary>
    public static void Prepare(DungeonSession game, int depth)
    {
        var hero = game.Hero;
        while (hero.Level < ExpectedLevel(depth))
            hero.Character.LevelUp(Rules.Rules.AttributeSet.BoostLevels.Contains(hero.Level + 1)
                ? [hero.Character.KeyAttribute, Rules.Rules.AttributeKind.Constitution, Rules.Rules.AttributeKind.Dexterity, Rules.Rules.AttributeKind.Wisdom]
                : null);
        hero.MythicTier = depth >= 91 ? Math.Min(Hero.MaxMythicTier, (depth - 90) / 2 + 1) : 0;
        var level = hero.Level;
        if (hero.Pack.Weapon is { } weapon)
        {
            var potency = level >= 16 ? 3 : level >= 10 ? 2 : level >= 2 ? 1 : 0;
            var striking = level >= 19 ? 3 : level >= 12 ? 2 : level >= 4 ? 1 : 0;
            weapon.ToHit = potency > 0 ? ItemBridge.PotencyThreshold(potency) : 0;
            weapon.ToDam = striking > 0 ? ItemBridge.StrikingThreshold(striking) : 0;
        }
        if (hero.Pack.InSlot(EquipSlot.Body) is { } body)
        {
            var potency = level >= 18 ? 3 : level >= 11 ? 2 : level >= 5 ? 1 : 0;
            body.ToAc = potency > 0 ? ItemBridge.PotencyThreshold(potency) : 0;
        }
        // What a hero has found worn by then (Angband's usual order: free action and true sight, the four base
        // resistances, then poison, blindness and confusion, then nether and hold life), as boons stand in for the items.
        (int From, string[] Boons)[] found =
        [
            (30, ["free_act", "see_invis"]),
            (45, ["resist:Fire", "resist:Cold", "resist:Electricity", "resist:Acid"]),
            (60, ["resist:Poison", "blind", "conf"]),
            (90, ["resist:Void", "hold_life"]),
        ];
        foreach (var (from, boons) in found.Where(f => depth >= f.From))
            hero.Boons.UnionWith(boons);
        var potion = depth >= 40 ? "potion_of_healing" : "potion_of_cure_critical_wounds";
        foreach (var (kind, count) in new[] { (potion, depth >= 95 ? 20 : depth >= 90 ? 15 : 10), ("teleportation", 4), ("phase_door", 5) })
        {
            var stack = game.Factory.Create(kind, count);
            game.Knowledge.LearnKind(stack.Kind);
            hero.Pack.Add(stack);
        }
        game.Belongings.Refit();
        hero.Character.Heal(hero.MaxHitPoints);
    }

    /// <summary>A bot game from a depth: prepared for it, then diving <paramref name="levels"/> levels on.</summary>
    public static CampaignResult Dive(Engine.Definitions.GameData data, ulong seed, int depth, int levels = 3, int maxTurns = 6000)
    {
        var game = new DungeonSession(data, seed);
        Prepare(game, depth);
        game.JumpTo(depth);
        new DiveBot(game) { Wary = true, ExploreBeforeDescending = 120 }.PlayTo(depth + levels, maxTurns);
        return new CampaignResult(depth, game.Hero.MaxDepth, game.IsOver, game.Won, game.Death?.Killer);
    }

    /// <summary>The finale: a hero ready for the bottom, from depth 99, against Saint Ysmere and then the Cinder of Ashur.</summary>
    public static CampaignResult Finale(Engine.Definitions.GameData data, ulong seed, int maxTurns = 12000)
    {
        var game = new DungeonSession(data, seed);
        Prepare(game, Story.YsmereDepth);
        game.JumpTo(Story.YsmereDepth);
        var bot = new DiveBot(game) { Hunt = true, Wary = true };
        for (var i = 0; i < maxTurns && !game.IsOver && !game.Won; i++)
            bot.Step();
        return new CampaignResult(Story.YsmereDepth, game.Hero.MaxDepth, game.IsOver, game.Won, game.Death?.Killer);
    }
}
