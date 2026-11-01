using WorldOfAngBand.Rules.Characters;

namespace WorldOfAngBand.Rules.Items;

/// <summary>The fundamental runes: weapons take potency and striking, body armour potency and resilient.</summary>
public enum RuneKind
{
    Potency,
    Striking,
    Resilient,
}

/// <summary>
/// The smith's rune work. An item's level sets the highest rank of each rune it can hold (the spec's rune table:
/// weapon potency from item level 2 / 10 / 16, striking 4 / 12 / 19, armour potency 5 / 11 / 18, resilient
/// 8 / 14 / 20). The smith <b>etches</b> a rune one rank higher for twice its value, and <b>moves</b> a rune from one
/// item to another of the same kind for half its value; whatever the receiving item held goes back the other way,
/// so nothing is lost. Charisma and Street Urchin haggle as at any trader.
/// </summary>
public static class Runework
{
    public const double EtchRate = 2.0;
    public const double MoveRate = 0.5;

    private static readonly int[] WeaponPotency = [2, 10, 16];
    private static readonly int[] Striking = [4, 12, 19];
    private static readonly int[] ArmorPotency = [5, 11, 18];
    private static readonly int[] Resilient = [8, 14, 20];

    /// <summary>The runes an item of this base can take at all.</summary>
    public static IReadOnlyList<RuneKind> Kinds(ItemDefinition d) =>
        d.IsWeapon ? [RuneKind.Potency, RuneKind.Striking] : d.IsBodyArmor ? [RuneKind.Potency, RuneKind.Resilient] : [];

    /// <summary>The highest rank of <paramref name="kind"/> an item of this base and level can hold (0: none).</summary>
    public static int Ceiling(ItemDefinition d, int itemLevel, RuneKind kind)
    {
        int[]? levels = (d.IsWeapon, d.IsBodyArmor, kind) switch
        {
            (true, _, RuneKind.Potency) => WeaponPotency,
            (true, _, RuneKind.Striking) => Striking,
            (_, true, RuneKind.Potency) => ArmorPotency,
            (_, true, RuneKind.Resilient) => Resilient,
            _ => null,
        };
        return levels?.Count(level => itemLevel >= level) ?? 0;
    }

    public static int Ceiling(Item item, RuneKind kind) => Ceiling(item.Definition, item.ItemLevel, kind);

    /// <summary>The item level a rune of this rank first fits on, for "needs item level N".</summary>
    public static int LevelFor(ItemDefinition d, RuneKind kind, int rank)
    {
        for (var level = 1; level <= 20; level++)
        {
            if (Ceiling(d, level, kind) >= rank)
                return level;
        }
        return 21;
    }

    /// <summary>What one rank of a rune adds to an item's value (<see cref="Pricing.Value"/>).</summary>
    public static int Value(RuneKind kind) => kind switch
    {
        RuneKind.Potency => 25,
        RuneKind.Striking => 40,
        _ => 30,
    };

    /// <summary>The rune's name on an item of this base: "potency", "striking", "resilient".</summary>
    public static string Name(RuneKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The rune at a rank, as the smith names it: "+1 potency", "striking", "greater striking".</summary>
    public static string Describe(RuneKind kind, int rank) => kind switch
    {
        RuneKind.Potency => $"+{rank} potency",
        _ => $"{rank switch { 1 => "", 2 => "greater ", _ => "major " }}{Name(kind)}",
    };

    /// <summary>What etching the next rank of <paramref name="kind"/> onto <paramref name="item"/> costs.</summary>
    public static int EtchPrice(Item item, RuneKind kind, Character c) =>
        Math.Max(1, (int)Math.Ceiling(EtchRate * Value(kind) * (item.Rune(kind) + 1) * Pricing.Haggle(c)));

    /// <summary>What moving <paramref name="from"/>'s <paramref name="kind"/> rune to another item costs.</summary>
    public static int MovePrice(Item from, RuneKind kind, Character c) =>
        Math.Max(1, (int)Math.Ceiling(MoveRate * Value(kind) * from.Rune(kind) * Pricing.Haggle(c)));

    /// <summary>Why the smith can't etch the next rank of <paramref name="kind"/> onto the item; null when he can.</summary>
    public static string? CannotEtch(Item item, RuneKind kind)
    {
        if (!Kinds(item.Definition).Contains(kind))
            return $"{item.Definition.Name} can't take a {Name(kind)} rune.";
        var next = item.Rune(kind) + 1;
        if (next > 3)
            return $"Its {Name(kind)} rune is already the strongest there is.";
        if (Ceiling(item, kind) < next)
            return $"{Capitalise(Describe(kind, next))} needs item level {LevelFor(item.Definition, kind, next)}.";
        return null;
    }

    /// <summary>Why the smith can't move <paramref name="from"/>'s <paramref name="kind"/> rune to <paramref name="to"/>; null when he can.</summary>
    public static string? CannotMove(Item from, Item to, RuneKind kind)
    {
        if (from == to)
            return "Choose two different items.";
        if (from.Rune(kind) == 0)
            return $"{from.Name} has no {Name(kind)} rune.";
        if (!Kinds(to.Definition).Contains(kind) || from.Definition.IsWeapon != to.Definition.IsWeapon)
            return $"A {(from.Definition.IsWeapon ? "weapon" : "armour")} rune can't go on {to.Definition.Name}.";
        if (to.Rune(kind) >= from.Rune(kind))
            return $"{to.Name} already has as strong a {Name(kind)} rune.";
        if (Ceiling(to, kind) < from.Rune(kind))
            return $"{Capitalise(Describe(kind, from.Rune(kind)))} needs item level {LevelFor(to.Definition, kind, from.Rune(kind))}.";
        return null;
    }

    /// <summary>Etches the next rank (no checks or payment: see <c>GameWorld.Etch</c>).</summary>
    internal static void Etch(Item item, RuneKind kind) => item.SetRune(kind, item.Rune(kind) + 1);

    /// <summary>Moves the rune across; the receiving item's weaker rune of the kind goes back the other way.</summary>
    internal static void Move(Item from, Item to, RuneKind kind)
    {
        var (given, taken) = (from.Rune(kind), to.Rune(kind));
        to.SetRune(kind, given);
        from.SetRune(kind, taken);
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
