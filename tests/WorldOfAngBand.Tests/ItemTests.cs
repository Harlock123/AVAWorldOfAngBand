using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Combat;

namespace WorldOfAngBand.Tests;

public class ItemTests
{
    private static DungeonSession NewGame(ulong seed = 31) => new(TestData.Data, seed);

    private static DungeonSession FirstLevel(ulong seed = 31)
    {
        var game = NewGame(seed);
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        return game;
    }

    private static Item Make(DungeonSession game, string kind, int number = 1) => game.Factory.Create(kind, number);

    private static Item Carry(DungeonSession game, Item item) => game.Hero.Pack.Add(item)!;

    [Fact]
    public void The_bridge_brings_in_weapons_armour_and_consumables_and_forty_two_egos()
    {
        var bridge = ItemBridge.Load(TestData.Data);
        Assert.Equal(42, bridge.Egos.Count);
        Assert.NotNull(bridge.WeaponFor(Make(NewGame(), "long_sword")));
        Assert.NotNull(bridge.ArmourFor(Make(NewGame(), "chain_mail")));
        Assert.Equal(4, bridge.ShieldFor(Make(NewGame(), "leather_shield"))!.Hardness);
        Assert.Equal("of the Pale Lantern", bridge.EgoName(TestData.Data.Egos.First(e => e.Id == "westernesse")));
    }

    [Fact]
    public void Only_bridged_kinds_and_egos_are_made_with_no_curses_or_artifacts()
    {
        var game = NewGame();
        for (var i = 0; i < 400; i++)
        {
            if (game.Factory.Make(game.Rng, 1 + i % 30, good: i % 3 == 0, great: i % 7 == 0) is not { } item)
                continue;
            Assert.True(game.ItemBridge.Has(item.Kind), item.Kind.Id);
            Assert.True(item.Ego is null || game.ItemBridge.Allows(item.Ego), item.Ego?.Id);
            Assert.Null(item.Artifact);
            Assert.Empty(item.Curses);
        }
    }

    [Fact]
    public void The_hero_starts_in_angband_kit_whose_pathfinder_numbers_come_from_the_bridge()
    {
        var game = NewGame();
        var hero = game.Hero;
        Assert.Equal("Long Sword", hero.Character.Weapon.Definition.Name);
        Assert.Equal(8, hero.Character.Weapon.Definition.DamageDie);
        Assert.Equal("Chain Mail", hero.Character.Armor.Definition.Name);
        Assert.Equal(18, hero.Character.ArmorClass().Total);
        Assert.Equal(2, hero.LightRadius);
        Assert.Equal(150, hero.Gold);
        Assert.Contains(hero.Pack.Pack, i => i.Kind.Id == "cure_light_wounds");
        Assert.Equal("a Long Sword (d8 slashing)", game.NameOf(hero.Pack.Weapon!));
    }

    [Fact]
    public void To_hit_and_to_dam_become_potency_and_striking()
    {
        var game = NewGame();
        var before = game.Hero.Character.AttackModifier().Total;
        game.Hero.Pack.Weapon!.ToHit = 8;
        game.Hero.Pack.Weapon!.ToDam = 10;
        game.Perform(new WieldItem(Carry(game, game.Hero.Pack.Remove(game.Hero.Pack.Weapon!, 1, () => game.Factory.NextSerial++))));
        Assert.Equal(2, game.Hero.Character.Weapon.Potency);
        Assert.Equal(2, game.Hero.Character.Weapon.Striking);
        Assert.Equal(before + 2, game.Hero.Character.AttackModifier().Total);
        Assert.Contains("+2 potency, greater striking", game.NameOf(game.Hero.Pack.Weapon!));
    }

    [Fact]
    public void A_two_handed_weapon_takes_the_shield_off()
    {
        var game = NewGame();
        var maul = Carry(game, Make(game, "maul"));
        Assert.True(game.Perform(new WieldItem(maul)));
        Assert.Null(game.Hero.Pack.InSlot(EquipSlot.Shield));
        Assert.Contains(game.Hero.Pack.Pack, i => i.Kind.Id == "leather_shield");
        Assert.Contains(game.Hero.Pack.Pack, i => i.Kind.Id == "long_sword");
        Assert.Equal(12, game.Hero.Character.Weapon.Definition.DamageDie);
    }

    [Fact]
    public void Gold_is_picked_up_on_the_way_and_things_with_g()
    {
        var game = FirstLevel();
        var step = DirectionExtensions.Compass.First(d => game.Level.IsEmptyFloor(game.Hero.Position.Step(d)) && game.FoeAt(game.Hero.Position.Step(d)) is null);
        var there = game.Hero.Position.Step(step);
        var gold = game.Factory.MakeGold(game.Rng, 1);
        game.Level.Objects.Add(there, gold);
        game.Level.Objects.Add(there, Make(game, "dagger"));
        var purse = game.Hero.Gold;
        game.Perform(new Walk(step));
        Assert.Equal(purse + gold.GoldValue, game.Hero.Gold);
        Assert.Single(game.Level.Objects.At(there));
        Assert.True(game.Perform(new PickUp()));
        Assert.Empty(game.Level.Objects.At(there));
        Assert.Contains(game.Hero.Pack.Pack, i => i.Kind.Id == "dagger");
    }

    [Fact]
    public void An_unknown_potion_is_learned_by_drinking_it()
    {
        // A healing potion no shop has on its shelves (what they stock is known on sight).
        var game = NewGame(32);
        var kind = TestData.Data.Objects.First(k => k.Base == "potion" && !game.Knowledge.IsAware(k)
            && game.ItemBridge.UseOf(k)?.StartsWith("heal:", StringComparison.Ordinal) == true);
        var potion = Carry(game, Make(game, kind.Id));
        Assert.EndsWith("Potion", game.NameOf(potion));
        game.HurtHero(10, "a test");
        var messages = new List<string>();
        game.Published += e => { if (e is MessageEvent m) messages.Add(m.Text); };
        Assert.True(game.Perform(new UseItem(potion)));
        Assert.True(game.Knowledge.IsAware(potion.Kind));
        Assert.Contains(messages, m => m.StartsWith("It was a Potion of ", StringComparison.Ordinal));
        Assert.Equal(game.Hero.MaxHitPoints, game.Hero.HitPoints);
    }

    [Fact]
    public void Phase_door_moves_the_hero_a_short_way()
    {
        var game = FirstLevel();
        var from = game.Hero.Position;
        var scroll = game.Hero.Pack.Pack.First(i => i.Kind.Id == "phase_door");
        Assert.True(game.Perform(new UseItem(scroll)));
        Assert.NotEqual(from, game.Hero.Position);
        Assert.InRange(from.DistanceTo(game.Hero.Position), 1, 10);
    }

    [Fact]
    public void Word_of_recall_takes_the_hero_to_town_and_back_to_their_deepest()
    {
        var game = FirstLevel();
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        Assert.Equal(2, game.Depth);
        var recall = Carry(game, Make(game, "scroll_of_word_of_recall", 2));
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        game.Perform(new UseItem(recall));
        for (var i = 0; i < 60 && game.Depth != 0; i++)
            game.Perform(new Hold());
        Assert.Equal(0, game.Depth);
        game.Perform(new UseItem(game.Hero.Pack.Pack.First(i => i.Kind.Id == "scroll_of_word_of_recall")));
        for (var i = 0; i < 60 && game.Depth == 0; i++)
            game.Perform(new Hold());
        Assert.Equal(2, game.Depth);
    }

    [Fact]
    public void A_flaming_weapon_burns_and_the_hit_teaches_its_rune()
    {
        var game = FirstLevel(33);
        var sword = Make(game, "long_sword");
        ObjectFactory.ApplyEgo(game.Rng, sword, TestData.Data.Egos.First(e => e.Id == "burning"), 5);
        Assert.Contains("{??}", game.NameOf(Carry(game, sword)));
        game.Perform(new WieldItem(game.Hero.Pack.Pack.First(i => i.Ego is not null)));
        var foe = Enumerable.Range(0, 1).Select(_ =>
        {
            var p = DirectionExtensions.Compass.Select(game.Hero.Position.Step).First(game.Level.IsEmptyFloor);
            game.Spawn(TestData.Data.Monster("white_jelly")!, p, asleep: true);
            return game.FoeAt(p)!;
        }).First();
        var toward = DirectionExtensions.FromOffset(foe.Monster.Position.X - game.Hero.Position.X, foe.Monster.Position.Y - game.Hero.Position.Y);
        var burned = false;
        game.Published += e => { if (e is MessageEvent m && m.Text.Contains(" fire)")) burned = true; };
        for (var i = 0; i < 30 && !burned && foe.Monster.IsActive; i++)
            game.Perform(new Walk(toward));
        Assert.True(burned);
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Brand("fire")));
        Assert.Contains("+1d6 fire", game.NameOf(game.Hero.Pack.Weapon!));
    }

    [Fact]
    public void Free_action_turns_a_paralysing_gaze_aside()
    {
        var game = FirstLevel(34);
        var gloves = Make(game, "leather_gloves");
        ObjectFactory.ApplyEgo(game.Rng, gloves, TestData.Data.Egos.First(e => e.Id == "free_action"), 5);
        game.Perform(new WieldItem(Carry(game, gloves)));
        Assert.True(game.Hero.Gear.Protects("free_act"));
        var p = DirectionExtensions.Compass.Select(game.Hero.Position.Step).First(game.Level.IsEmptyFloor);
        game.Spawn(TestData.Data.Monster("floating_eye")!, p, asleep: false);
        for (var i = 0; i < 40; i++)
            game.Perform(new Hold());
        Assert.False(game.Hero.Character.Conditions.Has(ConditionKind.Stunned));
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Resist("free_act")));
    }

    [Fact]
    public void Shops_sell_buy_and_the_home_keeps_things()
    {
        var game = NewGame();
        var apothecary = game.Stores["alchemist"];
        var potion = apothecary.Stock.First(i => i.Kind.Id == "cure_light_wounds");
        var price = game.Stores.PriceToBuy(apothecary, potion);
        var gold = game.Hero.Gold;
        Assert.True(game.Stores.Buy(apothecary, potion));
        Assert.Equal(gold - price, game.Hero.Gold);

        var smith = game.Stores["weaponsmith"];
        var dagger = Carry(game, Make(game, "dagger"));
        gold = game.Hero.Gold;
        var offer = game.Stores.PriceToSell(smith, dagger);
        Assert.True(offer > 0);
        Assert.True(game.Stores.Sell(smith, dagger));
        Assert.Equal(gold + offer, game.Hero.Gold);
        // The Apothecary has no use for a sword.
        Assert.False(game.Stores.Sell(apothecary, Carry(game, Make(game, "long_sword"))));

        var home = game.Stores["home"];
        var scroll = game.Hero.Pack.Pack.First(i => i.Kind.Id == "phase_door");
        gold = game.Hero.Gold;
        Assert.True(game.Stores.Sell(home, scroll));
        Assert.Equal(gold, game.Hero.Gold);
        Assert.Single(home.Stock);
        Assert.True(game.Stores.Buy(home, home.Stock[0]));
        Assert.Equal(gold, game.Hero.Gold);

        var black = game.Stores["black_market"];
        Assert.All(black.Stock, i => Assert.Equal(game.Stores.PriceToBuy(black, i), (int)Math.Max(1, ItemValue.Of(i, TestData.Data) * Stores.BlackMarketMarkup)));
    }

    [Fact]
    public void The_smith_etches_potency_at_level_two_for_gold()
    {
        var game = NewGame();
        var sword = game.Hero.Pack.Weapon!;
        var offer = game.Stores.Offer(sword, Etching.Potency)!;
        Assert.Equal(1, offer.Rank);
        Assert.NotNull(offer.Refusal);
        Assert.False(game.Stores.Etch(sword, Etching.Potency));
        game.Hero.Character.LevelUp();
        game.Hero.Gold = 1000;
        Assert.Null(game.Stores.Offer(sword, Etching.Potency)!.Refusal);
        Assert.True(game.Stores.Etch(sword, Etching.Potency));
        Assert.Equal(1, game.Hero.Character.Weapon.Potency);
        Assert.Equal(1000 - offer.Price, game.Hero.Gold);
        Assert.Equal(2, game.Stores.Offer(sword, Etching.Potency)!.Rank);
    }

    [Fact]
    public void Levels_have_things_on_the_floor_and_monsters_drop_what_they_carry()
    {
        var game = FirstLevel(35);
        Assert.True(game.Level.Objects.Count > 0);
        // Tamsin Barrowbell always drops two good things (DROP_2, DROP_GOOD, ONLY_ITEM).
        var p = DirectionExtensions.Compass.Select(game.Hero.Position.Step).First(game.Level.IsEmptyFloor);
        game.Spawn(TestData.Data.Monster("bullroarer")!, p, asleep: true);
        var near = game.Level.Objects.All.Count(o => o.Loc.ChebyshevTo(p) <= 3);
        game.Fight.Hurt(game.FoeAt(p)!, 10_000);
        Assert.Null(game.FoeAt(p));
        Assert.Equal(near + 2, game.Level.Objects.All.Count(o => o.Loc.ChebyshevTo(p) <= 3));
    }
}
