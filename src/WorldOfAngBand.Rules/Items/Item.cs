using System.Text;

namespace WorldOfAngBand.Rules.Items;

/// <summary>
/// One item: a base, a rarity, an item level, fundamental runes and rolled affixes. Weapons take potency (+1 to +3
/// to attack) and striking (extra damage dice); body armour takes potency (AC) and resilient (saves), each gated by
/// item level as in the spec's rune table.
/// </summary>
public sealed class Item
{
    private static int _nextId;

    public Item(ItemDefinition definition, Rarity rarity, int itemLevel, IReadOnlyList<AffixRoll>? affixes = null,
        int potency = 0, int striking = 0, int resilient = 0, string? name = null, int count = 1)
    {
        Count = Math.Clamp(count, 1, Math.Max(1, definition.Stack));
        Definition = definition;
        Rarity = rarity;
        ItemLevel = itemLevel;
        Affixes = affixes ?? [];
        Potency = potency;
        Striking = striking;
        Resilient = resilient;
        _name = name;
    }

    private readonly string? _name;

    public int Id { get; } = Interlocked.Increment(ref _nextId);
    public ItemDefinition Definition { get; }
    public Rarity Rarity { get; private set; }
    public int ItemLevel { get; }
    public IReadOnlyList<AffixRoll> Affixes { get; private set; }

    /// <summary>The forge's work: a new rarity and affixes (crafting; see <see cref="Crafting"/>).</summary>
    internal void Reforge(Rarity rarity, IReadOnlyList<AffixRoll> affixes)
    {
        Rarity = rarity;
        Affixes = affixes;
    }
    public int Potency { get; private set; }
    public int Striking { get; private set; }
    public int Resilient { get; private set; }

    /// <summary>A gun's refits at the forge: each holds one round more (at most <see cref="Crafting.MaxRefits"/>). Saved.</summary>
    public int Refits { get; internal set; }

    /// <summary>How many are stacked in this one (consumables; 1 for everything else).</summary>
    public int Count { get; internal set; }

    /// <summary>The player's mark: junk (to drop or sell in one go) or locked (never sold or dropped). Saved.</summary>
    public ItemMark Mark { get; set; }

    /// <summary>Junk, then locked, then unmarked (a Ctrl-click in the bag).</summary>
    public void CycleMark() => Mark = Mark switch
    {
        ItemMark.None => ItemMark.Junk,
        ItemMark.Junk => ItemMark.Locked,
        _ => ItemMark.None,
    };

    /// <summary>A rare's rolled name, or else the base's name with its runes and (uncommon) affixes.</summary>
    public string Name => _name ?? DefaultName();

    /// <summary>The rank of one fundamental rune.</summary>
    public int Rune(RuneKind kind) => kind switch
    {
        RuneKind.Potency => Potency,
        RuneKind.Striking => Striking,
        _ => Resilient,
    };

    /// <summary>Sets one fundamental rune's rank (the smith's work; see <see cref="Runework"/>).</summary>
    internal void SetRune(RuneKind kind, int rank)
    {
        switch (kind)
        {
            case RuneKind.Potency: Potency = rank; break;
            case RuneKind.Striking: Striking = rank; break;
            default: Resilient = rank; break;
        }
    }

    public Combat.DamageType? DamageType => Definition.IsWeapon ? Definition.DamageType : null;

    /// <summary>Magic: anything but a plain common item (runes, affixes, implicit powers, a trait).</summary>
    public bool IsMagical => Rarity != Rarity.Common || Potency > 0 || Striking > 0 || Resilient > 0
        || Definition.Implicit is not null || Definition.Effects is { Count: > 0 };

    public Weapon ToWeapon() => new(Definition.AsWeapon(), Potency, Striking);

    public Armor ToArmor() => new(Definition.AsArmor(), Potency, Resilient);

    private string DefaultName()
    {
        var name = new StringBuilder();
        if (Potency > 0)
            name.Append($"+{Potency} ");
        if (Striking > 0)
            name.Append(Striking switch { 1 => "Striking ", 2 => "Greater Striking ", _ => "Major Striking " });
        if (Rarity == Rarity.Uncommon && Affixes.FirstOrDefault(a => a.Definition.Prefix) is { } prefix)
            name.Append(prefix.Definition.Text).Append(' ');
        name.Append(Definition.Name);
        if (Rarity == Rarity.Uncommon && Affixes.FirstOrDefault(a => !a.Definition.Prefix) is { } suffix)
            name.Append(' ').Append(suffix.Definition.Text);
        return name.ToString();
    }

    /// <summary>The tooltip lines after the name: base, stats, runes, affixes.</summary>
    public IEnumerable<string> Describe()
    {
        var d = Definition;
        if (Mark == ItemMark.Junk)
            yield return "Marked as junk: Drop junk or Sell junk takes it";
        else if (Mark == ItemMark.Locked)
            yield return "Locked: it can't be sold or dropped";
        if (d.Category == ItemCategory.Key)
        {
            yield return "Carried · kept, never sold";
            if (d.Description is { } text)
                yield return text;
            yield break;
        }
        if (d.IsMaterial)
        {
            yield return Count > 1 ? $"Gunsmith's material · {Count} of {d.Stack}" : "Gunsmith's material";
            if (d.Description is { } use)
                yield return use;
            yield break;
        }
        if (d.Category == ItemCategory.Consumable)
        {
            yield return Count > 1 ? $"Consumable · {Count} of {d.Stack}" : "Consumable";
            if (d.Description is { } use)
                yield return use;
            if (d.IsBeltItem)
                yield return "Goes on the belt: keys 7 to 0.";
            yield break;
        }
        yield return Rarity switch
        {
            Rarity.Rare => $"{d.Name} · rare · item level {ItemLevel}",
            Rarity.Unique => $"{d.Name} · unique · item level {ItemLevel}",
            Rarity.Set => $"{d.Name} · set piece · item level {ItemLevel}",
            _ => $"{Rarity} {d.Category.ToString().ToLowerInvariant()} · item level {ItemLevel}",
        };
        if (d.IsShield)
        {
            yield return $"Shield · +{d.AcBonus} AC while raised · hardness {d.Hardness}";
            yield return "Raise a Shield to block: a blow loses its hardness.";
        }
        if (d.IsWeapon)
        {
            var dice = $"{1 + Striking}d{d.DamageDie}";
            yield return $"{dice} {d.DamageType.ToString().ToLowerInvariant()} · {d.AttacksPerSecond:0.0#} attacks/s";
            if (d.Traits is { Count: > 0 } traits)
                yield return string.Join(", ", traits);
            if (d.IsFirearm)
            {
                yield return $"Gun · {d.Range:0} tiles · {d.Capacity + Refits} round{(d.Capacity + Refits == 1 ? "" : "s")} · reload {d.ReloadSeconds:0.0#} s"
                    + (Refits > 0 ? $" · refitted {(Refits == 1 ? "once" : "twice")}" : "");
                if (d.IsPistol)
                    yield return "One-handed: a second pistol fits the off hand.";
            }
            if (Potency > 0)
                yield return $"+{Potency} item bonus to attack (potency rune)";
            if (Striking > 0)
                yield return $"{1 + Striking} damage dice (striking rune)";
        }
        else if (d.IsBodyArmor)
        {
            yield return $"{d.ArmorCategory} armour · AC +{d.AcBonus + Potency} · DEX cap {(d.DexCap >= 99 ? "none" : $"+{d.DexCap}")}";
            if (d.CheckPenalty != 0 || d.SpeedPenalty != 0)
                yield return $"Check penalty {d.CheckPenalty} · speed -{d.SpeedPenalty} ft · STR {d.Strength}";
            if (Resilient > 0)
                yield return $"+{Resilient} item bonus to saves (resilient rune)";
        }
        if (d.Implicit is { } imp)
        {
            if (imp.MaxHitPoints != 0) yield return $"+{imp.MaxHitPoints} maximum HP";
            if (imp.CastSpeed != 0) yield return $"+{imp.CastSpeed * 100:0}% cast speed";
            if (imp.ResourceRegen != 0) yield return $"+{imp.ResourceRegen * 100:0}% resource regeneration";
            if (imp.MoveSpeed != 0) yield return $"+{imp.MoveSpeed * 100:0}% movement speed";
        }
        foreach (var affix in Affixes)
            yield return affix.Describe();
        if (d.Trait is { } trait)
            yield return $"{trait}: {d.TraitText}";
        if (d.IsSpecial && d.Description is { } lore)
            yield return $"“{lore}”";
    }
}

/// <summary>A player's mark on an item in the bag.</summary>
public enum ItemMark
{
    None,
    Junk,
    Locked,
}
