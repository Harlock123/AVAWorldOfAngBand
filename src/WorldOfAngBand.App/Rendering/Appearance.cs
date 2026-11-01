using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Bridge;

namespace WorldOfAngBand.App.Rendering;

/// <summary>The paper doll: Flare's avatar layers for what a body wears (AshenReliquary's GameSession.LayersFor).</summary>
public static class Appearance
{
    /// <summary>The bare body, slot by slot, where nothing is worn.</summary>
    public static Dictionary<string, string> Bare() => new()
    {
        ["head"] = "head_short", ["chest"] = "default_chest", ["legs"] = "default_legs", ["feet"] = "default_feet", ["hands"] = "default_hands",
    };

    /// <summary>
    /// The layers for what the hero wears: each worn item's layer from the item bridge, and the legs dressed to match the
    /// body armour (Angband has no leg armour).
    /// </summary>
    public static IReadOnlyDictionary<string, string> For(Hero hero, ItemBridge bridge)
    {
        var layers = Bare();
        foreach (var item in hero.Pack.Equipped)
        {
            if (bridge.GfxFor(item) is not { } gfx)
                continue;
            var layer = item.Base.Slot switch
            {
                EquipSlot.Weapon => "main", EquipSlot.Shield => "off", EquipSlot.Head => "head", EquipSlot.Body => "chest",
                EquipSlot.Hands => "hands", EquipSlot.Feet => "feet", _ => null,
            };
            if (layer is not null)
                layers[layer] = gfx;
            if (item.Base.Slot == EquipSlot.Body && bridge.LegsFor(item) is { } legs)
                layers["legs"] = legs;
        }
        return layers;
    }

    /// <summary>A monster's paper doll: the bare body in the outfit its bridge entry gives it.</summary>
    public static IReadOnlyDictionary<string, string> For(IReadOnlyDictionary<string, string>? outfit)
    {
        var layers = Bare();
        foreach (var (slot, layer) in outfit ?? new Dictionary<string, string>())
            layers[slot] = layer;
        return layers;
    }
}
