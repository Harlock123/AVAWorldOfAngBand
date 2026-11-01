using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Items;

/// <summary>
/// The worn items and the 10x6 bag. Equipping updates the character at once: the main hand becomes the weapon,
/// the chest piece the armour, and every item's affixes and implicit bonuses are summed into its gear bonuses; a
/// Unique's trait and a Set's bonuses are handed to the character as worn effects.
/// <para>The Invested limit (spec, section 9): magic worn items other than weapons and shields are invested, and only
/// <see cref="InvestedLimit"/> of them work at once; any more (counted in slot order) lie <see cref="Dormant"/>.</para>
/// </summary>
public sealed class Equipment
{
    public const int BagColumns = 14;
    public const int BagRows = 6;
    public const int BagSize = BagColumns * BagRows;
    public const int InvestedLimit = 10;

    private readonly Character _character;
    private readonly Dictionary<EquipSlot, Item> _worn = [];
    private readonly Item?[] _bag = new Item?[BagSize];

    public Equipment(Character character) => _character = character;

    public IReadOnlyDictionary<EquipSlot, Item> Worn => _worn;
    public IReadOnlyList<Item?> Bag => _bag;
    public int Gold { get; private set; }

    /// <summary>Crafting materials, from salvage (see <see cref="Crafting"/>).</summary>
    public int Dust { get; private set; }
    public int Fragments { get; private set; }

    public void AddMaterials(int dust, int fragments)
    {
        Dust += Math.Max(0, dust);
        Fragments += Math.Max(0, fragments);
    }

    /// <summary>Spends materials; false (and nothing spent) when there aren't enough.</summary>
    public bool TrySpendMaterials(int dust, int fragments)
    {
        if (dust > Dust || fragments > Fragments)
            return false;
        Dust -= dust;
        Fragments -= fragments;
        return true;
    }

    /// <summary>Raised whenever what is worn changes (the paper doll redraws).</summary>
    public event Action? Changed;

    public Item? In(EquipSlot slot) => _worn.GetValueOrDefault(slot);

    public void AddGold(int amount) => Gold += Math.Max(0, amount);

    /// <summary>Pays <paramref name="amount"/> gold; false (and nothing spent) when there isn't enough.</summary>
    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > Gold)
            return false;
        Gold -= amount;
        return true;
    }

    /// <summary>Changes whenever the bag's contents do (the belt follows the consumables in it).</summary>
    public int BagVersion { get; private set; }

    /// <summary>
    /// Puts an item in the bag: a consumable tops up stacks of its kind first, the rest goes in the first free cell.
    /// False when it doesn't all fit (whatever did fit stays, and <paramref name="item"/> keeps the remainder).
    /// </summary>
    public bool TryAdd(Item item)
    {
        BagVersion++;
        if (item.Definition.Stack > 1)
        {
            foreach (var stack in _bag)
            {
                if (item.Count == 0)
                    return true;
                if (stack is null || stack == item || stack.Definition != item.Definition || stack.Count >= stack.Definition.Stack)
                    continue;
                var moved = Math.Min(item.Count, stack.Definition.Stack - stack.Count);
                stack.Count += moved;
                item.Count -= moved;
            }
            if (item.Count == 0)
                return true;
        }
        var free = Array.IndexOf(_bag, null);
        if (free < 0)
            return false;
        _bag[free] = item;
        return true;
    }

    public Item? TakeFromBag(int index)
    {
        BagVersion++;
        var item = _bag[index];
        _bag[index] = null;
        return item;
    }

    /// <summary>
    /// Moves the item in bag cell <paramref name="from"/> to cell <paramref name="to"/>: into an empty cell, onto a
    /// stack of the same consumable (as much as fits), or swapping with what is there. False when nothing moved.
    /// </summary>
    public bool Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= BagSize || to >= BagSize || _bag[from] is not { } item)
            return false;
        BagVersion++;
        if (_bag[to] is { } there && there.Definition == item.Definition && item.Definition.Stack > 1 && there.Count < there.Definition.Stack)
        {
            var moved = Math.Min(item.Count, there.Definition.Stack - there.Count);
            there.Count += moved;
            item.Count -= moved;
            if (item.Count == 0)
                _bag[from] = null;
            return true;
        }
        (_bag[from], _bag[to]) = (_bag[to], item);
        return true;
    }

    /// <summary>Packs the bag into <paramref name="order"/>, the first item in the first cell, stacks topped up.</summary>
    public void Sort(IComparer<Item> order)
    {
        var items = _bag.OfType<Item>().ToList();
        Array.Clear(_bag);
        BagVersion++;
        foreach (var item in items.OrderBy(i => i, order))
            TryAdd(item);
    }

    /// <summary>How many of a consumable the bag holds, across its stacks.</summary>
    public int CountOf(ItemDefinition definition) => _bag.Where(i => i?.Definition == definition).Sum(i => i!.Count);

    /// <summary>Uses up one of a consumable (from its smallest stack). False when there is none.</summary>
    public bool ConsumeOne(ItemDefinition definition)
    {
        var index = -1;
        for (var i = 0; i < _bag.Length; i++)
        {
            if (_bag[i]?.Definition == definition && (index < 0 || _bag[i]!.Count < _bag[index]!.Count))
                index = i;
        }
        if (index < 0)
            return false;
        BagVersion++;
        if (--_bag[index]!.Count <= 0)
            _bag[index] = null;
        return true;
    }

    /// <summary>
    /// The slot an item goes to: rings take the free hand (the first if both are full); a pistol beside a pistol in the
    /// main hand takes the empty off hand (twin pistols).
    /// </summary>
    public EquipSlot? SlotFor(Item item)
    {
        if (item.Definition.Slot is not { } slot)
            return null;
        if (slot is EquipSlot.Ring1 or EquipSlot.Ring2)
            return !_worn.ContainsKey(EquipSlot.Ring1) ? EquipSlot.Ring1 : !_worn.ContainsKey(EquipSlot.Ring2) ? EquipSlot.Ring2 : EquipSlot.Ring1;
        if (item.Definition.IsPistol && In(EquipSlot.MainHand) is { Definition.IsPistol: true } main && main != item && !_worn.ContainsKey(EquipSlot.OffHand))
            return EquipSlot.OffHand;
        return slot;
    }

    /// <summary>Wears an item straight away (starting gear). Whatever was in its slot is returned.</summary>
    public Item? Equip(Item item, EquipSlot? slot = null)
    {
        var target = slot ?? SlotFor(item) ?? throw new RulesException($"{item.Name} can't be worn.");
        _worn.TryGetValue(target, out var previous);
        _worn[target] = item;
        Refresh();
        return previous;
    }

    /// <summary>
    /// Wears the item in bag cell <paramref name="index"/> in <paramref name="slot"/> (dragged onto it), when it goes
    /// there: a ring in either ring slot, a pistol in the off hand beside a gun (twin pistols), anything else only in its own.
    /// </summary>
    public bool EquipFromBag(int index, EquipSlot slot)
    {
        if (_bag[index] is not { Definition.Slot: { } own } item)
            return false;
        var fits = own == slot || (own is EquipSlot.Ring1 or EquipSlot.Ring2 && slot is EquipSlot.Ring1 or EquipSlot.Ring2)
            || (slot == EquipSlot.OffHand && item.Definition.IsPistol && In(EquipSlot.MainHand) is { Definition.IsFirearm: true, Definition.IsTwoHanded: false });
        if (!fits)
            return false;
        if (slot == SlotFor(item))
            return EquipFromBag(index);
        BagVersion++;
        _bag[index] = Equip(item, slot);
        return true;
    }

    /// <summary>Takes off the item in <paramref name="slot"/> into bag cell <paramref name="index"/>, if that cell is free.</summary>
    public bool UnequipTo(EquipSlot slot, int index)
    {
        if (index < 0 || index >= BagSize || _bag[index] is not null || !_worn.TryGetValue(slot, out var item))
            return false;
        _worn.Remove(slot);
        _bag[index] = item;
        BagVersion++;
        Refresh();
        return true;
    }

    /// <summary>
    /// Wears the item in bag cell <paramref name="index"/>, swapping what was worn into that cell. A two-handed weapon
    /// takes the shield off, and a shield the two-handed weapon, into the bag; false when there is no room for it.
    /// </summary>
    public bool EquipFromBag(int index)
    {
        if (_bag[index] is not { } item || SlotFor(item) is not { } slot)
            return false;
        var clash = item.Definition.IsTwoHanded ? EquipSlot.OffHand
            : item.Definition.IsShield && In(EquipSlot.MainHand) is { Definition.IsTwoHanded: true } ? EquipSlot.MainHand
            : (EquipSlot?)null;
        var displaced = clash is { } other ? In(other) : null;
        BagVersion++;
        _bag[index] = null;
        var previous = Equip(item, slot);
        _bag[index] = previous;
        if (displaced is not null && clash is { } freed)
        {
            var free = Array.IndexOf(_bag, null);
            if (free < 0)
            {
                // No room for what has to come off: put everything back as it was.
                _bag[index] = item;
                if (previous is not null)
                    _worn[slot] = previous;
                else
                    _worn.Remove(slot);
                Refresh();
                return false;
            }
            _worn.Remove(freed);
            _bag[free] = displaced;
            Refresh();
        }
        return true;
    }

    /// <summary>Takes off the item in <paramref name="slot"/> into the bag. False when the bag is full.</summary>
    public bool Unequip(EquipSlot slot)
    {
        if (!_worn.TryGetValue(slot, out var item))
            return false;
        var free = Array.IndexOf(_bag, null);
        if (free < 0)
            return false;
        _worn.Remove(slot);
        _bag[free] = item;
        BagVersion++;
        Refresh();
        return true;
    }

    /// <summary>
    /// What wearing <paramref name="item"/> would do: the character's statistics now and with it worn in the slot it
    /// would take. Nothing changes; the gear is put back before returning.
    /// </summary>
    public IReadOnlyList<StatChange> PreviewEquip(Item item) =>
        SlotFor(item) is { } slot ? Preview(() => _worn[slot] = item) : [];

    /// <summary>What taking off the item in <paramref name="slot"/> would do to the character's statistics.</summary>
    public IReadOnlyList<StatChange> PreviewUnequip(EquipSlot slot) => Preview(() => _worn.Remove(slot));

    private IReadOnlyList<StatChange> Preview(Action change)
    {
        var before = StatSnapshot.Take(_character);
        var saved = new Dictionary<EquipSlot, Item>(_worn);
        var hp = _character.HitPoints;
        change();
        Apply();
        var after = StatSnapshot.Take(_character);
        _worn.Clear();
        foreach (var (slot, item) in saved)
            _worn[slot] = item;
        Apply();
        _character.RestoreHitPoints(hp);
        return StatSnapshot.Compare(before, after);
    }

    /// <summary>Re-applies weapon, armour and gear bonuses to the character.</summary>
    public void Refresh()
    {
        Apply();
        Changed?.Invoke();
    }

    private void Apply()
    {
        _character.Weapon = _worn.TryGetValue(EquipSlot.MainHand, out var weapon) ? weapon.ToWeapon() : new Weapon(Character.Fist);
        _character.Armor = _worn.TryGetValue(EquipSlot.Chest, out var armor) ? armor.ToArmor() : Armor.None;
        var active = Active.ToList();
        _character.ApplyGear(Sum(active), WornEffects(active));
    }

    /// <summary>Whether an item worn in a slot counts against the Invested limit: magic, and neither weapon nor shield.</summary>
    public static bool IsInvested(Item item, EquipSlot slot) => slot is not (EquipSlot.MainHand or EquipSlot.OffHand) && item.IsMagical;

    /// <summary>The invested items worn, in slot order.</summary>
    public IEnumerable<Item> Invested => _worn.OrderBy(kv => kv.Key).Where(kv => IsInvested(kv.Value, kv.Key)).Select(kv => kv.Value);

    /// <summary>Worn invested items past the limit: they do nothing until another comes off.</summary>
    public IReadOnlyCollection<Item> Dormant => Invested.Skip(InvestedLimit).ToList();

    /// <summary>The worn items that work: all of them but the dormant.</summary>
    public IEnumerable<Item> Active
    {
        get
        {
            var dormant = Dormant;
            return _worn.OrderBy(kv => kv.Key).Select(kv => kv.Value).Where(i => !dormant.Contains(i));
        }
    }

    /// <summary>How many pieces of a set work now.</summary>
    public int SetPieces(string setId) => Active.Where(i => i.Definition.Set == setId).Select(i => i.Definition.Id).Distinct().Count();

    /// <summary>
    /// The feat effects worn items bring: each Unique's or Set piece's trait (named for its trait or item), and each
    /// set's bonuses for the pieces worn (named "set (n)").
    /// </summary>
    private IReadOnlyList<(string Source, IReadOnlyList<FeatEffect> Effects)> WornEffects(IReadOnlyList<Item> items)
    {
        var effects = new List<(string, IReadOnlyList<FeatEffect>)>();
        foreach (var item in items)
        {
            if (item.Definition.Effects is { Count: > 0 } own)
                effects.Add((item.Definition.Trait ?? item.Definition.Name, own));
        }
        if (_character.Content?.Sets is { Count: > 0 } sets)
        {
            foreach (var group in items.Where(i => i.Definition.Set is not null).GroupBy(i => i.Definition.Set!))
            {
                if (!sets.TryGetValue(group.Key, out var set))
                    continue;
                var pieces = group.Select(i => i.Definition.Id).Distinct().Count();
                foreach (var bonus in set.Bonuses.Where(b => b.Pieces <= pieces))
                    effects.Add(($"{set.Name} ({bonus.Pieces})", bonus.Effects));
            }
        }
        return effects;
    }

    /// <summary>The gear bonuses a set of items adds up to.</summary>
    public static GearBonuses Sum(IEnumerable<Item> items)
    {
        int hp = 0, damage = 0;
        double move = 0, attack = 0, cast = 0, regen = 0, fury = 0;
        var resist = new Dictionary<DamageType, int>();
        var saves = new Dictionary<StatisticKind, int>();
        foreach (var item in items)
        {
            if (item.Definition.Implicit is { } imp)
            {
                hp += imp.MaxHitPoints;
                move += imp.MoveSpeed;
                attack += imp.AttackSpeed;
                cast += imp.CastSpeed;
                regen += imp.ResourceRegen;
            }
            foreach (var affix in item.Affixes)
            {
                var v = affix.Value;
                switch (affix.Definition.Stat)
                {
                    case AffixStat.MaxHitPoints: hp += v; break;
                    case AffixStat.MoveSpeed: move += v / 100.0; break;
                    case AffixStat.AttackSpeed: attack += v / 100.0; break;
                    case AffixStat.CastSpeed: cast += v / 100.0; break;
                    case AffixStat.ResourceRegen: regen += v / 100.0; break;
                    case AffixStat.FuryPerHit: fury += v; break;
                    case AffixStat.WeaponDamage: damage += v; break;
                    case AffixStat.Resistance when affix.Definition.Resist is { } type:
                        resist[type] = resist.GetValueOrDefault(type) + v;
                        break;
                    case AffixStat.Fortitude: saves[StatisticKind.Fortitude] = saves.GetValueOrDefault(StatisticKind.Fortitude) + v; break;
                    case AffixStat.Reflex: saves[StatisticKind.Reflex] = saves.GetValueOrDefault(StatisticKind.Reflex) + v; break;
                    case AffixStat.Will: saves[StatisticKind.Will] = saves.GetValueOrDefault(StatisticKind.Will) + v; break;
                }
            }
        }
        return new GearBonuses
        {
            MaxHitPoints = hp, MoveSpeed = move, AttackSpeed = attack, CastSpeed = cast, ResourceRegen = regen,
            FuryPerHit = fury, WeaponDamage = damage, Resistances = resist, Saves = saves,
        };
    }
}
