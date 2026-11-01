using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Items;

/// <summary>
/// What things cost. An item's value grows with its item level, rarity, runes and affixes; traders sell at twice
/// the value and buy at a quarter of it. Charisma haggles (2% per point of the modifier, either way, at most 10%)
/// and a Street Urchin background takes another 5% off what you pay.
/// </summary>
public static class Pricing
{
    public const double SellMarkup = 2.0;
    public const double BuyRate = 0.25;

    /// <summary>The item's value in gold before any trader's cut.</summary>
    public static int Value(Item item)
    {
        var d = item.Definition;
        if (d.Category == ItemCategory.Consumable && d.Price > 0)
            return d.Price * item.Count;
        double basePrice = d.IsJewelry ? 20 : d.IsWeapon ? 12 : d.IsBodyArmor ? 10 + 4 * d.AcBonus : d.IsShield ? 8 + 3 * d.AcBonus + d.Hardness : 7;
        var rarity = item.Rarity switch
        {
            Rarity.Common => 1.0,
            Rarity.Uncommon => 2.5,
            Rarity.Rare => 6.0,
            _ => 12.0,
        };
        var value = basePrice * rarity * (1 + 0.35 * (Math.Max(1, item.ItemLevel) - 1))
            + Runework.Value(RuneKind.Potency) * item.Potency + Runework.Value(RuneKind.Striking) * item.Striking
            + Runework.Value(RuneKind.Resilient) * item.Resilient + 4 * item.Affixes.Count;
        return Math.Max(1, (int)Math.Round(value));
    }

    /// <summary>The share of the price a character pays: 1.0 is list price.</summary>
    public static double Haggle(Character c)
    {
        var factor = 1 - Math.Clamp(c.Attributes[AttributeKind.Charisma] * 0.02, -0.10, 0.10);
        if (c.Background.Id == "street-urchin")
            factor -= 0.05;
        return factor;
    }

    /// <summary>What a trader asks for the item; <paramref name="scale"/> is the trader's own terms (1 at list price).</summary>
    public static int BuyPrice(Item item, Character c, double scale = 1) => Math.Max(1, (int)Math.Ceiling(Value(item) * SellMarkup * Haggle(c) * scale));

    /// <summary>What a trader pays for the item; selling something back costs the same within a visit (buyback).</summary>
    public static int SellPrice(Item item) => Math.Max(1, (int)Math.Floor(Value(item) * BuyRate));

    /// <summary>
    /// One belt item from a trader's supplies: its price, more as the hero's level heightens what it does (a bomb's dice
    /// grow every two levels), through the haggle.
    /// </summary>
    public static int SupplyPrice(ItemDefinition item, Character c, double scale = 1) =>
        Math.Max(1, (int)Math.Ceiling(item.Price * SellMarkup * (1 + 0.5 * (Abilities.AbilityDefinition.SpellRank(c.Level) - 1)) * Haggle(c) * scale));

    /// <summary>One healing potion charge from a trader.</summary>
    public static int PotionPrice(Character c, double scale = 1) => Math.Max(1, (int)Math.Ceiling((10 + 5 * c.Level) * Haggle(c) * scale));
}
