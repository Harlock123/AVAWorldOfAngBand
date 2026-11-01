using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Rules.Items;
using Item = WorldOfAngBand.Engine.Items.Item;
using EquipSlot = WorldOfAngBand.Engine.Definitions.EquipSlot;

namespace WorldOfAngBand.Game;

/// <summary>
/// The hero's things: picking up and dropping, wielding and taking off, and learning what they are. Learning follows
/// Angband 4.2: a kind is known once used; a rune (to-hit, a brand, a resistance…) once it shows itself, and then on
/// every item that has it. What is obvious on wielding (speed, stealth, light, a stat) is learned at once.
/// </summary>
internal sealed class Belongings(DungeonSession session)
{
    private Hero Hero => session.Hero;
    private Inventory Pack => session.Hero.Pack;

    /// <summary>Picks up everything here that fits. False when there is nothing to pick up.</summary>
    public bool PickUp()
    {
        var pile = session.Level.Objects.At(Hero.Position).ToList();
        if (pile.Count == 0)
        {
            session.Say("There is nothing here to pick up.");
            return false;
        }
        foreach (var item in pile)
        {
            if (item.IsGold)
            {
                TakeGold(item);
                continue;
            }
            if (!Pack.CanCarry(item))
            {
                session.Say($"You have no room for {Name(item)}.");
                continue;
            }
            session.Level.Objects.Remove(Hero.Position, item);
            session.Knowledge.See(item);
            var stack = Pack.Add(item)!;
            session.Say($"You have {Name(stack)}.");
        }
        session.Publish(new ItemsChangedEvent());
        return true;
    }

    /// <summary>Walking onto a square: gold is picked up at once; anything else is named.</summary>
    public void Arrive()
    {
        var pile = session.Level.Objects.At(Hero.Position).ToList();
        foreach (var gold in pile.Where(i => i.IsGold))
            TakeGold(gold);
        var rest = pile.Where(i => !i.IsGold).ToList();
        foreach (var item in rest)
            session.Knowledge.See(item);
        if (rest.Count == 1)
            session.Say($"You see {Name(rest[0])}.");
        else if (rest.Count > 1)
            session.Say($"You see a pile of {rest.Count} things.");
        if (pile.Count > 0)
            session.Publish(new ItemsChangedEvent());
    }

    private void TakeGold(Item gold)
    {
        session.Level.Objects.Remove(Hero.Position, gold);
        Hero.Gold += gold.GoldValue;
        session.Say($"You have found {gold.GoldValue} gold pieces worth of {ItemNaming.Plain(gold.Kind.Name, false)}.");
    }

    public bool Drop(Item item, int count = 1)
    {
        if (!Pack.Contains(item))
            return false;
        if (Pack.Equipped.Contains(item))
        {
            Pack.TakeOff(item);
            Refit();
        }
        var dropped = Pack.Remove(item, Math.Clamp(count, 1, item.Number), () => session.Factory.NextSerial++);
        session.Loot.DropNear(Hero.Position, dropped);
        session.Say($"You drop {Name(dropped)}.");
        session.Publish(new ItemsChangedEvent());
        return true;
    }

    /// <summary>
    /// Wields or wears an item from the pack or the floor; what it replaces goes to the pack (or the floor). A weapon held
    /// in two hands takes the shield off, and a shield the two-handed weapon, as Pathfinder has it.
    /// </summary>
    public bool Wield(Item item)
    {
        if (!item.IsWearable || Pack.SlotFor(item) < 0)
        {
            session.Say($"You cannot wear {Name(item)}.");
            return false;
        }
        var onFloor = !Pack.Contains(item);
        if (onFloor)
            session.Level.Objects.Remove(Hero.Position, item);
        var twoHanded = session.ItemBridge.WeaponFor(item)?.Has(WeaponTrait.TwoHand) == true;
        if (twoHanded && Pack.InSlot(EquipSlot.Shield) is { } shield)
            Remove(shield);
        if (item.Base.Slot == EquipSlot.Shield && Pack.Weapon is { } held && session.ItemBridge.WeaponFor(held)?.Has(WeaponTrait.TwoHand) == true)
            Remove(held);
        var slot = Pack.SlotFor(item);
        var previous = Pack.Wield(item, () => session.Factory.NextSerial++);
        if (previous is not null)
            Stow(previous);
        var worn = Pack.Equipment[slot]!;
        session.Say($"You are {(item.Base.IsWeapon ? "wielding" : "wearing")} {Name(worn)}.");
        LearnObvious(worn);
        Refit();
        session.Publish(new ItemsChangedEvent());
        return true;
    }

    public bool TakeOff(Item item)
    {
        if (!Pack.Equipped.Contains(item))
            return false;
        Remove(item);
        Refit();
        session.Publish(new ItemsChangedEvent());
        return true;
    }

    private void Remove(Item item)
    {
        if (Pack.TakeOff(item))
        {
            session.Say($"You take off {Name(item)}.");
            return;
        }
        // No room in the pack: it goes on the floor.
        Pack.Remove(item, item.Number, () => session.Factory.NextSerial++);
        session.Loot.DropNear(Hero.Position, item);
        session.Say($"You take off {Name(item)} and put it down.");
    }

    private void Stow(Item item)
    {
        if (Pack.Add(item) is null)
        {
            session.Loot.DropNear(Hero.Position, item);
            session.Say($"You have no room for {Name(item)}, and put it down.");
        }
    }

    /// <summary>Works the hero's Pathfinder numbers out again from what they wear.</summary>
    public void Refit() => Hero.Gear.Apply(Hero, session.ItemBridge);

    /// <summary>Angband 4.2: on wielding, the modifiers (speed, stealth, light, stats) and regeneration show themselves.</summary>
    private void LearnObvious(Item item)
    {
        foreach (var (mod, value) in item.Modifiers)
            if (value != 0)
                Learn(RuneIds.Modifier(mod));
        if (item.Flags.Contains(ItemFlags.Regen))
            Learn(RuneIds.Flag(ItemFlags.Regen));
    }

    /// <summary>Learns a rune; the first time, the hero is told.</summary>
    public void Learn(string rune)
    {
        if (session.Knowledge.LearnRune(rune))
            session.Say($"You have learned the rune of {RuneName(rune)}.");
    }

    /// <summary>Learns every rune of an item (selling it, or a full identification).</summary>
    public void LearnAll(Item item)
    {
        session.Knowledge.LearnKind(item.Kind);
        foreach (var rune in item.Runes())
            session.Knowledge.LearnRune(rune);
    }

    public string Name(Item item) => ItemText.Describe(item, session.Knowledge, session.ItemBridge);

    public static string RuneName(string rune) => rune switch
    {
        RuneIds.ToHit => "accuracy (potency)",
        RuneIds.ToDam => "striking",
        RuneIds.ToAc => "armour potency",
        _ when rune.StartsWith("brand:", StringComparison.Ordinal) => rune[6..] switch
        {
            "fire" => "flame", "cold" => "frost", "acid" => "corrosion", "elec" => "shock", "pois" => "venom", var e => e,
        },
        _ when rune.StartsWith("slay:", StringComparison.Ordinal) => $"bane of {rune[5..].ToLowerInvariant()}",
        _ when rune.StartsWith("resist:", StringComparison.Ordinal) => rune[7..] switch
        {
            "fire" => "fire resistance", "cold" => "cold resistance", "acid" => "acid resistance", "elec" => "electricity resistance",
            "pois" => "poison resistance", "free_act" => "free action", "fear" => "courage", "blind" => "sight", "conf" => "clarity",
            "see_invis" => "true sight", var r => r.Replace('_', ' '),
        },
        _ when rune.StartsWith("mod:", StringComparison.Ordinal) => rune[4..],
        _ when rune.StartsWith("flag:", StringComparison.Ordinal) => ItemFlags.Name(rune[5..]),
        _ => rune,
    };
}
