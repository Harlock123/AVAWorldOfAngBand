using WorldOfAngBand.Rules.Characters;

namespace WorldOfAngBand.Rules.Items;

/// <summary>How well an item suits the character carrying it.</summary>
public enum ItemFit
{
    /// <summary>Worn gear the character can use well.</summary>
    Usable,

    /// <summary>A bomb or elixir for the belt.</summary>
    Belt,

    /// <summary>Gear the class can't use well: untrained body armour, a caster's focus on a fighter or a blade on a
    /// wizard, a shield for a class that never raises one, a weapon a rogue can't sneak attack with.</summary>
    PoorFit,

    /// <summary>Kept and never sold (the pilgrim's lantern).</summary>
    Key,
}

/// <summary>Whether wearing an item would improve on what is worn now.</summary>
public enum ItemComparison
{
    /// <summary>Nothing to compare (a belt item, a key, a poor fit).</summary>
    None,
    Better,
    Worse,
    /// <summary>Better in some ways, worse in others.</summary>
    Mixed,
    /// <summary>Changes nothing the character would notice.</summary>
    Same,
}

/// <summary>An item judged for a character: its fit, and how it compares with what is worn.</summary>
public sealed record ItemVerdict(ItemFit Fit, ItemComparison Comparison)
{
    /// <summary>The bag's sort group: upgrades first, then other usable gear, belt items, poor fits, keys.</summary>
    public int SortGroup => Fit switch
    {
        ItemFit.Usable => Comparison == ItemComparison.Better ? 0 : 1,
        ItemFit.Belt => 2,
        ItemFit.PoorFit => 3,
        _ => 4,
    };
}

/// <summary>
/// Judges items for the character carrying them, for the bag's upgrade arrows, its red tint on poor fits and its
/// Sort. The comparison is the tooltip's own (<see cref="Equipment.PreviewEquip"/>): better when every statistic that
/// changes rises, worse when every one falls. A shield's AC counts only while raised, so shields compare on AC and
/// hardness instead.
/// </summary>
public static class ItemJudge
{
    /// <param name="raisesShields">Whether the character has a way to raise a shield (the fighter's bar).</param>
    public static ItemVerdict Judge(Character character, Equipment gear, Item item, bool raisesShields)
    {
        var d = item.Definition;
        if (d.Category == ItemCategory.Key)
            return new(ItemFit.Key, ItemComparison.None);
        if (d.IsBeltItem)
            return new(ItemFit.Belt, ItemComparison.None);
        if (d.Slot is null)
            return new(ItemFit.Usable, ItemComparison.None);
        if (IsPoorFit(character, d, raisesShields))
            return new(ItemFit.PoorFit, ItemComparison.None);
        if (gear.Worn.Values.Contains(item))
            return new(ItemFit.Usable, ItemComparison.Same);
        return new(ItemFit.Usable, Compare(gear, item));
    }

    private static bool IsPoorFit(Character c, ItemDefinition d, bool raisesShields)
    {
        if (d.IsBodyArmor && !c.Class.IsTrainedIn(d.ArmorCategory))
            return true;
        if (d.IsShield)
            return !raisesShields;
        if (d.IsWeapon)
        {
            var caster = c.Class.Resource.StartsWith("Mana", StringComparison.OrdinalIgnoreCase);
            if (caster)
                return !d.CasterFocus;
            // Sneak attack needs an agile or finesse weapon (or a ranged one).
            if (c.Class.SneakAttack && !d.Ranged && d.Traits?.Any(t => t is WeaponTrait.Agile or WeaponTrait.Finesse) != true)
                return true;
            return d.CasterFocus;
        }
        return false;
    }

    private static ItemComparison Compare(Equipment gear, Item item)
    {
        var d = item.Definition;
        if (d.IsShield)
        {
            if (gear.In(EquipSlot.OffHand) is not { Definition.IsShield: true } worn)
                return ItemComparison.Better;
            var ac = d.AcBonus.CompareTo(worn.Definition.AcBonus);
            var hardness = d.Hardness.CompareTo(worn.Definition.Hardness);
            var affixes = Directions(gear.PreviewEquip(item));
            return Combine([ac, hardness, .. affixes]);
        }
        return Combine(Directions(gear.PreviewEquip(item)));
    }

    private static IEnumerable<int> Directions(IReadOnlyList<StatChange> preview) => preview.Select(c => c.Direction);

    private static ItemComparison Combine(IEnumerable<int> directions)
    {
        var list = directions.Where(d => d != 0).ToList();
        if (list.Count == 0)
            return ItemComparison.Same;
        if (list.All(d => d > 0))
            return ItemComparison.Better;
        return list.All(d => d < 0) ? ItemComparison.Worse : ItemComparison.Mixed;
    }

    /// <summary>
    /// The bag's order: upgrades, other usable gear, belt items, poor fits, keys; within each, the most valuable first,
    /// then by name.
    /// </summary>
    public static IComparer<Item> SortOrder(Func<Item, ItemVerdict> judge) => Comparer<Item>.Create((a, b) =>
    {
        var group = judge(a).SortGroup.CompareTo(judge(b).SortGroup);
        if (group != 0)
            return group;
        var value = Pricing.Value(b).CompareTo(Pricing.Value(a));
        return value != 0 ? value : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
    });
}
