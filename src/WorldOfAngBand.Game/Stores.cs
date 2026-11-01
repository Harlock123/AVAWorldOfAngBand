using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Game.Bridge;

namespace WorldOfAngBand.Game;

/// <summary>One of Gallowmere's shops (or the hero's room at the inn), with what it has on its shelves.</summary>
public sealed class Store(StoreDef definition, string name, string owner)
{
    public StoreDef Definition { get; } = definition;

    public string Id => Definition.Id;

    public string Name { get; } = name;

    public string Owner { get; } = owner;

    public bool IsHome => Definition.Home;

    public List<Item> Stock { get; } = [];
}

/// <summary>A rune the Smith can etch: potency on a weapon or armour, striking on a weapon.</summary>
public enum Etching
{
    Potency,
    Striking,
}

/// <summary>What the Smith offers for an item: the rank, the price, and why not (when it can't be done).</summary>
public sealed record EtchOffer(Etching Kind, int Rank, int Price, string? Refusal);

/// <summary>
/// Gallowmere's shops (AVABand's stores, with Angband 4.2.5's stock tables): each stocks what its table lists that is in
/// the game so far, plain and lightly enchanted; the Hanged Man (the black market) stocks good things at three times the
/// price. Prices are Angband's values; the shops buy at half. The Smith also does AshenReliquary's runework: it etches
/// potency and striking up to the next rank, at the hero levels Pathfinder's item levels give, for gold. The shelves are
/// filled again each time the hero comes back from the dungeon.
/// </summary>
public sealed class Stores
{
    /// <summary>Who keeps each shop: the Ashen setting's people in place of Angband's owners.</summary>
    private static readonly Dictionary<string, string> Owners = new()
    {
        ["general"] = "Hob Larrow, the quartermaster",
        ["armoury"] = "Brannoc, who was a shieldman at Ironhold",
        ["weaponsmith"] = "Edda Cole, the runesmith",
        ["bookseller"] = "Wren, keeper of the Archive",
        ["alchemist"] = "Ysolde the herbalist",
        ["magic"] = "Old Mother Vey",
        ["black_market"] = "Tobias Rook, who asks no questions",
        ["home"] = "",
    };

    /// <summary>A weapon's potency ranks, their prices in gold, and the hero level each needs (Pathfinder's item levels 2, 10, 16).</summary>
    private static readonly (int Price, int Level)[] WeaponPotency = [(400, 2), (4000, 10), (30000, 16)];

    /// <summary>Striking ranks (item levels 4, 12, 19).</summary>
    private static readonly (int Price, int Level)[] StrikingRanks = [(650, 4), (6500, 12), (50000, 19)];

    /// <summary>Armour potency ranks (item levels 5, 11, 18).</summary>
    private static readonly (int Price, int Level)[] ArmourPotency = [(500, 5), (5000, 11), (40000, 18)];

    public const int SellDivisor = 2;
    public const int BlackMarketMarkup = 3;

    private readonly DungeonSession _session;
    private readonly Dictionary<string, Store> _stores = [];

    internal Stores(DungeonSession session)
    {
        _session = session;
        foreach (var def in session.Data.Stores)
            _stores[def.Id] = new Store(def, Setting.ShopName(def.Id), Owners.GetValueOrDefault(def.Id, ""));
        Restock();
    }

    public Store this[string id] => _stores[id];

    public IReadOnlyCollection<Store> All => _stores.Values;

    /// <summary>Fills the shelves again (the home keeps what the hero left).</summary>
    public void Restock()
    {
        var rng = _session.Rng;
        var factory = _session.Factory;
        foreach (var store in _stores.Values.Where(s => !s.IsHome))
        {
            store.Stock.Clear();
            var def = store.Definition;
            bool Ok(string id) => _session.Data.Objects.FirstOrDefault(k => k.Id == id) is { } kind && _session.ItemBridge.Has(kind);
            foreach (var id in def.Staples.Where(Ok))
            {
                var item = factory.Create(id, 1);
                item.Number = item.Base.MaxStack > 1 ? rng.RandRange(3, 8) : 1;
                store.Stock.Add(item);
            }
            var stocked = def.Stocked.Where(Ok).ToList();
            var count = def.BlackMarket ? rng.RandRange(def.MinItems, def.MaxItems) : Math.Min(stocked.Count, rng.RandRange(Math.Max(4, def.MinItems), Math.Max(6, def.MaxItems)));
            for (var i = 0; i < count; i++)
            {
                Item? item;
                if (def.BlackMarket)
                    item = factory.Make(rng, 25, good: true);
                else if (stocked.Count > 0)
                {
                    item = factory.Create(rng.Pick(stocked), 1);
                    factory.ApplyMagic(rng, item, 5);
                }
                else
                    item = null;
                if (item is null)
                    continue;
                if (item.Base.MaxStack > 1 && !item.IsWearable)
                    item.Number = rng.RandRange(1, 5);
                if (store.Stock.FirstOrDefault(s => s.CanStackWith(item)) is { } same)
                    same.Absorb(item);
                else
                    store.Stock.Add(item);
            }
            // As in Angband, what a shop has on its shelves is known for what it is: seeing a potion there teaches its flavour.
            foreach (var item in store.Stock)
                _session.Knowledge.LearnKind(item.Kind);
        }
    }

    /// <summary>What one of an item costs to buy here.</summary>
    public int PriceToBuy(Store store, Item item) =>
        (int)Math.Max(1, ItemValue.Of(item, _session.Data) * (store.Definition.BlackMarket ? BlackMarketMarkup : 1));

    /// <summary>What a shop pays for one of the hero's items: half its worth (nothing for what it doesn't deal in).</summary>
    public int PriceToSell(Store store, Item item) =>
        Buys(store, item) ? (int)(ItemValue.Of(item, _session.Data) / SellDivisor) : 0;

    /// <summary>Whether a shop deals in a kind of item (the home takes anything).</summary>
    public bool Buys(Store store, Item item) =>
        store.IsHome || store.Definition.BlackMarket || store.Definition.Buys.Contains(item.Base.Id) || store.Definition.AvabandBuys.Contains(item.Base.Id);

    /// <summary>Buys (or, at home, takes back) one of a stock item. False with a reason said when it can't be done.</summary>
    public bool Buy(Store store, Item item)
    {
        var hero = _session.Hero;
        // Lean Times: only the Quartermaster and the hero's room trade.
        if (_session.Birth.Twist == "lean" && !store.IsHome && store.Id != "general")
        {
            _session.Say("The shutters are down this week.");
            return false;
        }
        var price = store.IsHome ? 0 : PriceToBuy(store, item);
        if (hero.Gold < price)
        {
            _session.Say("You do not have enough gold.");
            return false;
        }
        var one = item.Number > 1 ? item.Split(_session.Factory.NextSerial++, 1) : item;
        if (!hero.Pack.CanCarry(one))
        {
            if (one != item)
                item.Absorb(one);
            _session.Say("You cannot carry that many items.");
            return false;
        }
        if (one == item)
            store.Stock.Remove(item);
        hero.Gold -= price;
        // What a shop sells is known: the shopkeeper says what it is.
        _session.Belongings.LearnAll(one);
        var stack = hero.Pack.Add(one)!;
        _session.Say(store.IsHome ? $"You have {_session.Belongings.Name(stack)}." : $"You bought {_session.Belongings.Name(one)} for {price} gold.");
        _session.Publish(new ItemsChangedEvent());
        return true;
    }

    /// <summary>Sells (or, at home, leaves) one of the hero's items. Selling it teaches the hero all it is.</summary>
    public bool Sell(Store store, Item item)
    {
        var hero = _session.Hero;
        if (!Buys(store, item))
        {
            _session.Say("I don't buy that sort of thing.");
            return false;
        }
        if (store.IsHome && store.Stock.Count >= store.Definition.Capacity && store.Stock.All(s => !s.CanStackWith(item)))
        {
            _session.Say("Your room is full.");
            return false;
        }
        if (hero.Pack.Equipped.Contains(item))
        {
            hero.Pack.TakeOff(item);
            _session.Belongings.Refit();
        }
        var price = store.IsHome ? 0 : PriceToSell(store, item);
        var one = hero.Pack.Remove(item, 1, () => _session.Factory.NextSerial++);
        hero.Pack.Prune();
        if (!store.IsHome)
            _session.Belongings.LearnAll(one);
        hero.Gold += price;
        if (store.Stock.FirstOrDefault(s => s.CanStackWith(one)) is { } same)
            same.Absorb(one);
        else
            store.Stock.Add(one);
        _session.Say(store.IsHome ? $"You leave {_session.Belongings.Name(one)}." : $"You sold {_session.Belongings.Name(one)} for {price} gold.");
        _session.Publish(new ItemsChangedEvent());
        return true;
    }

    /// <summary>What the Smith would etch on an item next, and for how much.</summary>
    public EtchOffer? Offer(Item item, Etching kind)
    {
        var bridge = _session.ItemBridge;
        var weapon = bridge.WeaponFor(item) is not null;
        var armour = bridge.ArmourFor(item) is not null;
        if (kind == Etching.Striking && !weapon || kind == Etching.Potency && !weapon && !armour)
            return null;
        var current = kind == Etching.Striking ? ItemBridge.Striking(item.ToDam) : ItemBridge.Potency(weapon ? item.ToHit : item.ToAc);
        var ranks = kind == Etching.Striking ? StrikingRanks : weapon ? WeaponPotency : ArmourPotency;
        if (current >= ranks.Length)
            return new EtchOffer(kind, current, 0, "It bears the strongest rune I can etch.");
        var (price, level) = ranks[Math.Max(0, current)];
        var refusal = _session.Hero.Level < level ? $"You must be level {level} to bear that rune." : null;
        return new EtchOffer(kind, current + 1, price, refusal);
    }

    /// <summary>Etches the next rank of a rune: raises the Angband bonus behind it to that rank's threshold.</summary>
    public bool Etch(Item item, Etching kind)
    {
        if (Offer(item, kind) is not { Refusal: null } offer)
        {
            _session.Say(Offer(item, kind)?.Refusal ?? "The Smith shakes her head.");
            return false;
        }
        if (_session.Hero.Gold < offer.Price)
        {
            _session.Say("You do not have enough gold.");
            return false;
        }
        _session.Hero.Gold -= offer.Price;
        var weapon = _session.ItemBridge.WeaponFor(item) is not null;
        if (kind == Etching.Striking)
        {
            item.ToDam = Math.Max(item.ToDam, Bridge.ItemBridge.StrikingThreshold(offer.Rank));
            _session.Belongings.Learn(RuneIds.ToDam);
        }
        else if (weapon)
        {
            item.ToHit = Math.Max(item.ToHit, Bridge.ItemBridge.PotencyThreshold(offer.Rank));
            _session.Belongings.Learn(RuneIds.ToHit);
        }
        else
        {
            item.ToAc = Math.Max(item.ToAc, Bridge.ItemBridge.PotencyThreshold(offer.Rank));
            _session.Belongings.Learn(RuneIds.ToAc);
        }
        _session.Belongings.Refit();
        _session.Say($"Edda etches the rune into {_session.Belongings.Name(item)} for {offer.Price} gold.");
        _session.Publish(new ItemsChangedEvent());
        return true;
    }
}
