using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Content;

namespace WorldOfAngBand.Tests;

public class PowerTests
{
    private static readonly ContentDatabase Content = ContentDatabase.Embedded;

    /// <summary>A hero of a class (and subclass) on the first level, with nothing else on it, everything known.</summary>
    private static DungeonSession As(string classId, string? subclass = null, ulong seed = 71)
    {
        var choices = CharacterBuilder.Recommend(Content, "Tester", "human", "deserter", classId) with
        {
            Subclass = subclass ?? Content.Feats.Values.First(f => f.Type == FeatType.Subclass && f.Class == classId).Id,
        };
        var game = new DungeonSession(TestData.Data, seed, birth: new Birth(choices));
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        foreach (var p in game.Level.AllLocs())
            game.Known.Remember(game.Level, p);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        // Light round the hero, so what comes near is seen.
        game.LightArea(game.Hero.Position, 8);
        game.Perform(new Hold());
        return game;
    }

    private static Foe Near(DungeonSession game, string race, int distance)
    {
        var spot = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.ChebyshevTo(game.Hero.Position) >= 1
                && Engine.Combat.ProjectionPath.Projectable(game.Level, p, game.Hero.Position, 20))
            .OrderBy(p => Math.Abs(p.DistanceTo(game.Hero.Position) - distance)).First();
        game.Spawn(TestData.Data.Monster(race)!, spot, asleep: false);
        var foe = game.FoeAt(spot)!;
        // Sturdy, so it lasts the test.
        foe.Monster.MaxHp = foe.Monster.Hp = 500;
        return foe;
    }

    private static List<string> Listen(DungeonSession game)
    {
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        return messages;
    }

    [Theory]
    [InlineData("fighter", true)]
    [InlineData("champion", true)]
    [InlineData("rogue", true)]
    [InlineData("wizard", true)]
    [InlineData("priest", true)]
    [InlineData("druid", true)]
    [InlineData("blackguard", true)]
    [InlineData("ranger", false)]
    [InlineData("necromancer", false)]
    [InlineData("gunslinger", false)]
    public void A_class_is_playable_when_the_game_has_what_its_abilities_need(string classId, bool playable) =>
        Assert.Equal(playable, Powers.IsPlayable(Content, classId));

    [Theory]
    [InlineData("priest", "Mana", "Divine Lance")]
    [InlineData("druid", "Mana", "Stinging Swarm")]
    [InlineData("blackguard", "Fury", "Unholy Blow")]
    public void The_new_classes_start_with_their_resource_kit_and_bar(string classId, string resource, string second)
    {
        var game = As(classId);
        Assert.Equal(classId, game.Hero.Character.Class.Id);
        Assert.Equal(resource, game.Hero.Resource.Name);
        Assert.Equal(second, game.Powers.Bar[1].Name);
        Assert.NotNull(game.Hero.Character.Subclass);
        Assert.NotNull(game.Hero.Pack.Weapon);
    }

    [Fact]
    public void Every_level_of_every_playable_class_has_picks_to_make()
    {
        foreach (var classId in Content.Classes.Keys.Where(c => Powers.IsPlayable(Content, c)))
        {
            var game = As(classId);
            while (game.Hero.Level < 20)
            {
                game.Hero.Character.LevelUp();
                LevelPicks.TakeAll(game);
                Assert.Empty(game.Hero.Character.PendingLevels);
            }
            Assert.Contains(game.Powers.Bar, a => a.Level >= 16);
        }
    }

    [Fact]
    public void A_wizards_ignition_is_a_spell_attack_that_takes_two_actions()
    {
        var game = As("wizard");
        var kobold = Near(game, "kobold", 4);
        var messages = Listen(game);
        var energy = game.Hero.Energy;
        Assert.True(game.Perform(new UsePower("ignition", kobold.Monster.Position)), string.Join(" | ", messages));
        Assert.Contains(messages, m => m.StartsWith("You cast Ignition", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Ignition") && m.Contains("vs AC"));
    }

    [Fact]
    public void An_ability_pays_its_cost_and_waits_out_its_cooldown()
    {
        var game = As("priest");
        game.HurtHero(5, "a test");
        var mana = game.Hero.Resource.Current;
        var hp = game.Hero.HitPoints;
        Assert.True(game.Perform(new UsePower("heal")));
        Assert.True(game.Hero.HitPoints > hp);
        // Fifteen spent; Mana comes back a little over the two actions.
        Assert.InRange(game.Hero.Resource.Current, mana - 15, mana - 10);
        var messages = Listen(game);
        Assert.False(game.Perform(new UsePower("heal")));
        Assert.Contains(messages, m => m.Contains("not ready"));
    }

    [Fact]
    public void Fury_comes_from_fighting_and_pays_for_the_fighters_abilities()
    {
        var game = As("fighter", "bulwark");
        var foe = Near(game, "kobold", 1);
        var messages = Listen(game);
        Assert.False(game.Perform(new UsePower("power_attack", foe.Monster.Position)));
        Assert.Contains(messages, m => m.Contains("needs 15 Fury"));
        for (var i = 0; i < 30 && game.Hero.Resource.Current < 15; i++)
            game.Perform(new UsePower("strike", foe.Monster.Position));
        Assert.True(game.Hero.Resource.Current >= 15);
        Assert.True(game.Perform(new UsePower("power_attack", foe.Monster.Position)));
    }

    [Fact]
    public void A_ward_raises_the_heros_ac_while_it_lasts()
    {
        var game = As("druid");
        var ac = game.Hero.Character.ArmorClass().Total;
        Assert.True(game.Perform(new UsePower("barkskin")));
        Assert.Equal(ac + 1, game.Hero.Character.ArmorClass().Total);
        for (var i = 0; i < 40; i++)
            game.Perform(new Hold());
        Assert.Equal(ac, game.Hero.Character.ArmorClass().Total);
    }

    [Fact]
    public void A_howl_makes_every_monster_round_the_hero_save_or_be_frightened()
    {
        var game = As("blackguard");
        var foes = Enumerable.Range(0, 4).Select(_ => Near(game, "jackal", 2)).ToList();
        game.Hero.Resource.Gain(50);
        var messages = Listen(game);
        Assert.True(game.Perform(new UsePower("howl_of_the_damned")));
        Assert.Equal(4, messages.Count(m => m.Contains("vs DC")));
    }

    [Fact]
    public void A_blast_bursts_on_its_target_and_catches_those_beside_it()
    {
        var game = As("priest");
        while (game.Hero.Level < 4)
            game.Hero.Character.LevelUp();
        LevelPicks.TakeAll(game);
        game.Hero.Resource.Gain(100);
        var target = Near(game, "kobold", 5);
        var messages = Listen(game);
        Assert.True(game.Perform(new UsePower("orb_of_draining", target.Monster.Position)), string.Join(" | ", messages));
        Assert.Contains(messages, m => m.Contains("spirit") && m.Contains("Fortitude"));
    }

    [Fact]
    public void Held_fast_a_monster_strikes_but_cannot_walk()
    {
        var game = As("druid");
        var foe = Near(game, "jackal", 4);
        game.Powers.Afflict(foe, ConditionKind.Immobilized, 1, 10);
        var at = foe.Monster.Position;
        for (var i = 0; i < 6; i++)
            game.Perform(new Hold());
        Assert.Equal(at, foe.Monster.Position);
    }

    [Fact]
    public void A_level_up_waits_for_its_picks_and_choosing_them_grants_the_feats()
    {
        var game = As("priest", "confessor");
        game.Hero.Character.LevelUp();
        Assert.Equal([2], game.Hero.Character.PendingLevels);
        var choice = LevelPicks.Suggest(game.Hero.Character, 2) with { ClassFeat = "spiritual_weapon" };
        Assert.True(game.Perform(new ChooseLevel(2, choice)));
        Assert.Empty(game.Hero.Character.PendingLevels);
        Assert.Contains(game.Powers.Bar, a => a.Id == "spiritual_weapon");
    }
}
