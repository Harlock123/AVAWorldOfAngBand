namespace WorldOfAngBand.Rules.Items;

/// <summary>The hub's stash: 60 slots of storage that stay put while the hero travels. Saved with the character.</summary>
public sealed class Stash
{
    public const int Columns = 10;
    public const int Rows = 6;
    public const int Size = Columns * Rows;

    private readonly Item?[] _slots = new Item?[Size];

    public IReadOnlyList<Item?> Slots => _slots;

    /// <summary>Puts an item in the first free slot; false when the stash is full.</summary>
    public bool TryAdd(Item item)
    {
        var free = Array.IndexOf(_slots, null);
        if (free < 0)
            return false;
        _slots[free] = item;
        return true;
    }

    /// <summary>Puts an item in slot <paramref name="index"/> if it is free (dragged there); false otherwise.</summary>
    public bool TryAddAt(Item item, int index)
    {
        if (index < 0 || index >= Size || _slots[index] is not null)
            return false;
        _slots[index] = item;
        return true;
    }

    /// <summary>Moves or swaps the items in two slots.</summary>
    public bool Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Size || to >= Size || _slots[from] is null)
            return false;
        (_slots[from], _slots[to]) = (_slots[to], _slots[from]);
        return true;
    }

    /// <summary>Packs the stash into <paramref name="order"/>, from the first slot.</summary>
    public void Sort(IComparer<Item> order)
    {
        var items = _slots.OfType<Item>().OrderBy(i => i, order).ToList();
        Array.Clear(_slots);
        for (var i = 0; i < items.Count; i++)
            _slots[i] = items[i];
    }

    /// <summary>Takes the item out of a slot, or null if it is empty.</summary>
    public Item? Take(int index)
    {
        if (index < 0 || index >= Size || _slots[index] is not { } item)
            return null;
        _slots[index] = null;
        return item;
    }

    internal void Restore(IEnumerable<Item?> items)
    {
        var i = 0;
        foreach (var item in items.Take(Size))
            _slots[i++] = item;
    }
}
