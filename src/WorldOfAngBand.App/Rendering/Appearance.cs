using WorldOfAngBand.Rules.Items;

namespace WorldOfAngBand.App.Rendering;

/// <summary>The paper doll: Flare's avatar layers for what a body wears (AshenReliquary's GameSession.LayersFor).</summary>
public static class Appearance
{
    /// <summary>The bare body, slot by slot, where nothing is worn.</summary>
    public static Dictionary<string, string> Bare() => new()
    {
        ["head"] = "head_short", ["chest"] = "default_chest", ["legs"] = "default_legs", ["feet"] = "default_feet", ["hands"] = "default_hands",
    };

    /// <summary>The layers for what the hero wears.</summary>
    public static IReadOnlyDictionary<string, string> For(Equipment gear)
    {
        var layers = Bare();
        foreach (var (slot, item) in gear.Worn)
        {
            if (item.Definition.Gfx is not { } gfx)
                continue;
            // A bow is held in the off hand, as Flare draws it; a gun in the weapon hand, and a second pistol in the
            // off hand has a layer of its own.
            if (slot == EquipSlot.OffHand && item.Definition.IsPistol)
                gfx += "_offhand";
            var layer = slot switch
            {
                EquipSlot.MainHand when item.Definition.Ranged && !item.Definition.IsFirearm => "off",
                EquipSlot.MainHand => "main", EquipSlot.OffHand => "off", EquipSlot.Head => "head", EquipSlot.Chest => "chest",
                EquipSlot.Hands => "hands", EquipSlot.Legs => "legs", EquipSlot.Feet => "feet", _ => null,
            };
            if (layer is not null)
                layers[layer] = gfx;
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
