using WorldOfAngBand.Data;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.Tests;

public class ModTests
{
    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "worldofangband-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void The_example_mod_brings_its_monster_and_dagger_into_the_game_through_its_bridge()
    {
        var mod = ModCatalog.WriteExample(TempRoot())!;
        var data = DataLoader.Load(DataLoader.DefaultDataDirectory, DataLoader.SettingDirectory, mod);
        Assert.Equal(mod, data.ModDirectories[^1]);
        var game = new DungeonSession(data, 101);
        Assert.True(game.Bridge.Has(data.Monster("example_mud_newt")!));
        Assert.True(game.ItemBridge.Has(data.Object("example_pick_knife")!));
        Assert.NotNull(game.ItemBridge.WeaponFor(game.Factory.Create("example_pick_knife")));
        // The shipped bridge is all still there.
        Assert.True(game.Bridge.Has(data.Monster("kobold")!));
        Assert.True(game.ItemBridge.Has(data.Object("long_sword")!));
        var info = ModCatalog.Inspect(mod);
        Assert.Empty(info.Errors);
        Assert.DoesNotContain(info.Notes, n => n.Contains("bridge"));
    }

    [Fact]
    public void A_mods_names_go_over_the_settings_own()
    {
        var mod = Path.Combine(TempRoot(), "names");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "ashen_names.json"), """{ "monsters": { "kobold": "ash-kobold" } }""");
        var names = Setting.MonsterNamesWith([DataLoader.SettingDirectory, mod]);
        Assert.Equal("ash-kobold", names["kobold"]);
        Assert.Equal(Setting.RenamedMonsters["fang"], names["fang"]);
    }

    [Fact]
    public void A_mod_switched_off_is_left_out()
    {
        var root = TempRoot();
        var mod = ModCatalog.WriteExample(root)!;
        var data = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [DataLoader.SettingDirectory, mod], [ModCatalog.ExampleName], out var results);
        Assert.Null(data.Monster("example_mud_newt"));
        Assert.Equal(ModLoadState.Disabled, results.Single(r => r.Name == ModCatalog.ExampleName).State);
    }
}
