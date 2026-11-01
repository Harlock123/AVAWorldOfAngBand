using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Randomness;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Bridge;

namespace WorldOfAngBand.Tests;

public class SpellTests
{
    private static DungeonSession FirstLevel(ulong seed)
    {
        var game = new DungeonSession(TestData.Data, seed);
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        foreach (var p in game.Level.AllLocs())
            game.Known.Remember(game.Level, p);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        return game;
    }

    /// <summary>Puts an awake monster a few squares off with a clear line to the hero.</summary>
    private static Foe InSight(DungeonSession game, string race, int distance = 3)
    {
        var spot = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.DistanceTo(game.Hero.Position) >= 2
                && Engine.Combat.ProjectionPath.Projectable(game.Level, p, game.Hero.Position, 20))
            .OrderBy(p => Math.Abs(p.DistanceTo(game.Hero.Position) - distance)).First();
        game.Spawn(TestData.Data.Monster(race)!, spot, asleep: false);
        return game.FoeAt(spot)!;
    }

    private static List<string> Listen(DungeonSession game)
    {
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        return messages;
    }

    [Fact]
    public void Angbands_damage_formulas_work_at_a_spell_power()
    {
        var cold = TestData.Data.MonsterSpell("BO_COLD")!;
        var rng = new GameRandom(1);
        for (var i = 0; i < 50; i++)
            Assert.InRange(MonsterSpells.Formula(cold.DamageFormula!, cold.FormulaTerms, 9, rng), 3 + 6, 3 + 48);
        var arrow = TestData.Data.MonsterSpell("ARROW")!;
        Assert.InRange(MonsterSpells.Formula(arrow.DamageFormula!, arrow.FormulaTerms, 16, rng), 3, 18);
    }

    [Theory]
    [InlineData(5, 1.0)]
    [InlineData(20, 0.8)]
    [InlineData(40, 0.55)]
    [InlineData(100, 0.3)]
    public void Damage_and_hit_points_scale_down_past_depth_ten(int depth, double scale) =>
        Assert.Equal(scale, CreatureBridge.DamageScale(depth), 3);

    [Fact]
    public void A_deep_monsters_hit_points_are_scaled()
    {
        var game = FirstLevel(41);
        var dragon = InSight(game, "ancient_red_dragon", 6);
        var race = dragon.Monster.Race;
        Assert.True(dragon.Monster.MaxHp <= (int)Math.Ceiling(race.HitPoints * 1.5 * CreatureBridge.DamageScale(race.Depth)));
        Assert.True(dragon.Monster.MaxHp < race.HitPoints);
    }

    [Fact]
    public void An_archer_shoots_from_afar_against_the_heros_ac()
    {
        var game = FirstLevel(42);
        InSight(game, "kobold_archer", 4);
        var messages = Listen(game);
        for (var i = 0; i < 40 && !messages.Any(m => m.Contains("fires an arrow")); i++)
            game.Perform(new Hold());
        Assert.Contains(messages, m => m.Contains("fires an arrow"));
        Assert.Contains(messages, m => m.Contains("vs AC"));
    }

    [Fact]
    public void A_breath_calls_for_a_basic_reflex_save()
    {
        var game = FirstLevel(43);
        InSight(game, "baby_blue_dragon", 3);
        var messages = Listen(game);
        for (var i = 0; i < 80 && !messages.Any(m => m.Contains("(Reflex")) && !game.IsOver; i++)
            game.Perform(new Hold());
        Assert.Contains(messages, m => m.Contains("(Reflex") && m.Contains("vs DC"));
    }

    [Fact]
    public void Courage_turns_a_scare_spell_aside()
    {
        var game = FirstLevel(44);
        game.Hero.Timed["heroism"] = 500;
        game.Hero.Gear.Apply(game.Hero, game.ItemBridge);
        Assert.True(game.Hero.Gear.Protects("fear"));
        // An acolyte casts Scare (and heals); give it many chances.
        InSight(game, "acolyte", 3);
        for (var i = 0; i < 60 && !game.IsOver; i++)
            game.Perform(new Hold());
        Assert.False(game.Hero.Character.Conditions.Has(Rules.Combat.ConditionKind.Frightened));
    }

    [Fact]
    public void Summons_call_up_monsters_of_their_kind()
    {
        var game = FirstLevel(45);
        var before = game.Foes.Count();
        var placed = game.SummonNear(game.Hero.Position, 3, 10, r => r.Base == "kobold");
        Assert.InRange(placed, 1, 3);
        Assert.Equal(before + placed, game.Foes.Count());
        Assert.All(game.Foes, f => Assert.Equal("kobold", f.Monster.Race.Base));
    }
}
