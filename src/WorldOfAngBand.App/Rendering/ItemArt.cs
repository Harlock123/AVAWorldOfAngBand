using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Items;
using Item = WorldOfAngBand.Engine.Items.Item;

namespace WorldOfAngBand.App.Rendering;

/// <summary>
/// How an item lying on the floor is drawn: one of Flare's loot sprites (AshenReliquary's), by what the item is, or —
/// for what Flare has none of (scrolls, torches, cloaks…) — its DCSS tile.
/// </summary>
public static class ItemArt
{
    /// <summary>The Flare loot sprite for an item, or null when it has none.</summary>
    public static string? Loot(Item item, ItemBridge bridge)
    {
        if (item.IsGold)
            return item.GoldValue >= 100 ? "coins100" : item.GoldValue >= 25 ? "coins25" : "coins5";
        if (bridge.WeaponFor(item) is not null)
            return bridge.GfxFor(item) switch
            {
                "dagger" or "shortsword" or "longsword" or "greatsword" or "battle_axe" or "hand_axe" or "mace" or "war_hammer" or "maul"
                    or "staff" or "greatstaff" or "club" or "reinforced_club" or "rod" => bridge.GfxFor(item),
                _ => "longsword",
            };
        if (bridge.ArmourFor(item) is { } armour)
            return armour.Category is ArmorCategory.Medium or ArmorCategory.Heavy ? "steel_armor" : "leather_armor";
        return item.Base.Id switch
        {
            "shield" => bridge.ShieldFor(item)?.Hardness >= 6 ? "shield" : "buckler",
            "boots" => "boots",
            "gloves" or "helm" => "clothes",
            "ring" => "ring",
            "amulet" => "gem",
            "potion" => item.Kind.Id.Contains("cure") || item.Kind.Id.Contains("healing") ? "hp_potion" : "mp_potion",
            _ => null,
        };
    }

    /// <summary>The DCSS tile key for an item (its kind's, else its base's).</summary>
    public static IEnumerable<string> TileKeys(Item item)
    {
        yield return $"object:{item.Kind.Id}";
        yield return $"object-base:{item.Base.Id}";
    }
}
