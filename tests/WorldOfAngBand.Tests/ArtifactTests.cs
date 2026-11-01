using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Abilities;
using WorldOfAngBand.Rules.Combat;

namespace WorldOfAngBand.Tests;

public class ArtifactTests
{
    private static DungeonSession FirstLevel(ulong seed, Birth? birth = null)
    {
        var game = new DungeonSession(TestData.Data, seed, birth: birth);
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        game.LightArea(game.Hero.Position, 8);
        game.Perform(new Hold());
        return game;
    }

    [Fact]
    public void Only_artifacts_on_kinds_in_the_game_can_be_made()
    {
        var game = new DungeonSession(TestData.Data, 81);
        Assert.NotEmpty(game.Factory.Artifacts);
        Assert.All(game.Factory.Artifacts, a => Assert.True(game.ItemBridge.Has(TestData.Data.Object(a.Kind)!)));
        Assert.DoesNotContain(game.Factory.Artifacts, a => a.Id == "the_one_ring");
    }

    [Fact]
    public void An_artifact_goes_by_its_name_in_the_setting()
    {
        var game = new DungeonSession(TestData.Data, 82);
        var ringil = game.Factory.CreateArtifact(TestData.Data.Artifacts.First(a => a.Id == "ringil"));
        game.Belongings.LearnAll(ringil);
        var name = game.NameOf(ringil);
        Assert.Contains("Coldflame", name);
        Assert.DoesNotContain("Ringil", name);
    }

    [Fact]
    public void Randarts_make_a_new_set_for_the_game_named_in_the_setting()
    {
        var game = new DungeonSession(TestData.Data, 83, birth: new Birth(Randarts: true));
        Assert.All(game.Factory.Artifacts.Where(a => a.Id.StartsWith("randart_", StringComparison.Ordinal)), a => Assert.DoesNotContain(a.Name, TestData.Data.Artifacts.Select(s => s.Name)));
        Assert.Contains(game.Factory.Artifacts, a => a.Id.StartsWith("randart_", StringComparison.Ordinal));
        var again = new DungeonSession(TestData.Data, 83, birth: new Birth(Randarts: true));
        Assert.Equal(game.Factory.Artifacts.Select(a => a.Name), again.Factory.Artifacts.Select(a => a.Name));
    }

    [Theory]
    [InlineData("ball:cold:100:2", AbilityKind.Blast, DamageType.Cold)]
    [InlineData("bolt:fire:9d8", AbilityKind.SpellBolt, DamageType.Fire)]
    [InlineData("drain_life:90", AbilityKind.SpellBolt, DamageType.Void)]
    public void An_attacking_activation_is_a_one_off_ability(string text, AbilityKind kind, DamageType type)
    {
        var activation = Activation.Parse(text, 40, "it");
        Assert.Equal(kind, activation.Attack!.Kind);
        Assert.Equal(type, activation.Attack.DamageType);
        // Angband's damage, scaled for depth as a monster's is.
        Assert.True(activation.Attack.DamageAt(1).Average < (text.Contains("100") ? 100 : 90));
    }

    [Fact]
    public void Other_activations_are_the_games_own_effects()
    {
        var activation = Activation.Parse("heal:40:0; cure:blind; cure:cut; timed:fast:1d20+20; probe", 30, "it");
        Assert.Null(activation.Attack);
        Assert.Equal(["heal:22", "cure:Blinded", "haste:20"], activation.Effects);
        Assert.True(Activation.Parse("probe", 30, "it").IsEmpty);
    }

    [Fact]
    public void A_worn_artifact_activates_then_recharges()
    {
        var game = FirstLevel(71);
        var art = TestData.Data.Artifacts.First(a => a.Id == "ringil");
        var sword = game.Factory.CreateArtifact(art);
        game.Belongings.LearnAll(sword);
        game.Hero.Pack.Add(sword);
        Assert.True(game.Perform(new WieldItem(sword)));
        var spot = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.ChebyshevTo(game.Hero.Position) >= 2
                && Engine.Combat.ProjectionPath.Projectable(game.Level, p, game.Hero.Position, 20))
            .OrderBy(p => Math.Abs(p.DistanceTo(game.Hero.Position) - 4)).First();
        game.Spawn(TestData.Data.Monster("kobold")!, spot, asleep: false);
        game.Perform(new Hold());
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        Assert.True(game.Perform(new ActivateItem(sword, spot)), string.Join(" | ", messages));
        Assert.True(sword.Timeout > 0);
        Assert.False(game.Perform(new ActivateItem(sword, spot)));
        Assert.Contains(messages, m => m.Contains("still charging"));
    }
}
