namespace WorldOfAngBand.Game;

/// <summary>
/// Names in the Ashen setting for things the 4.2.5 data names otherwise (the spec's section 3: renaming is data in the
/// end, data/setting/names.json; these few come first).
/// </summary>
public static class Setting
{
    private static readonly Dictionary<string, string> Shops = new()
    {
        ["general"] = "Quartermaster",
        ["armoury"] = "Armourer",
        ["weaponsmith"] = "Smith",
        ["bookseller"] = "The Lantern Archive",
        ["alchemist"] = "Apothecary",
        ["magic"] = "Curio Dealer",
        ["black_market"] = "The Hanged Man",
        ["home"] = "Your Room at the Inn",
        ["inn"] = "The Lantern Inn",
        ["caravan"] = "Caravan",
        ["artificer"] = "Artificer",
    };

    /// <summary>A Gallowmere shop's name, by the shop id its entrance carries.</summary>
    public static string ShopName(string shopId) => Shops.GetValueOrDefault(shopId, shopId);
}
