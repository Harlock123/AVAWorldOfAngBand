using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Game.Bridge;

namespace WorldOfAngBand.Game;

/// <summary>
/// An item's name as the hero knows it: Angband's name (flavoured until the kind is known, the ego's name in the Ashen
/// setting once its runes are), then what it is in Pathfinder's terms, as far as its runes are known. "{??}" marks
/// runes still to learn, as in Angband 4.2.
/// </summary>
public static class ItemText
{
    private static readonly Dictionary<string, string> Protections = new()
    {
        ["free_act"] = "free action", ["fear"] = "fearless", ["blind"] = "sight kept", ["conf"] = "clear mind",
        ["see_invis"] = "see invisible", ["hold_life"] = "hold life", ["stun"] = "steady",
        ["sust_str"] = "Strength kept", ["sust_dex"] = "Dexterity kept", ["sust_con"] = "Constitution kept",
        ["sust_int"] = "Intelligence kept", ["sust_wis"] = "Wisdom kept",
    };

    private static readonly Dictionary<string, string> Elements = new()
    {
        ["fire"] = "fire", ["cold"] = "cold", ["acid"] = "acid", ["elec"] = "electricity", ["pois"] = "poison",
    };

    public static string Describe(Item item, PlayerKnowledge knowledge, ItemBridge bridge, bool withArticle = true)
    {
        var name = ItemNaming.Describe(item, knowledge, withArticle, full: false);
        // The setting's name for a kind Angband names after Tolkien (mithril is moonsilver).
        if (Setting.ObjectName(item.Kind.Id) is { } renamed)
            name = name.Replace(ItemNaming.Plain(item.Kind.Name, item.Number != 1), ItemNaming.Plain(renamed, item.Number != 1), StringComparison.Ordinal);
        if (item.Ego is { } ego && knowledge.IsFullyKnown(item))
            name = name.Replace(ego.Name, bridge.EgoName(ego), StringComparison.Ordinal);
        // An artifact's name in the setting ('Ringil' is 'Coldflame').
        if (item.Artifact is { } art && Setting.ArtifactName(art.Id) is { } artName)
            name = name.Replace(art.Name, artName, StringComparison.Ordinal);
        if (item.IsGold)
            return name;
        var parts = new List<string>();
        if (bridge.WeaponFor(item) is { } weapon)
        {
            parts.Add($"d{weapon.DamageDie} {weapon.DamageType.ToString().ToLowerInvariant()}");
            if (knowledge.KnowsRune(RuneIds.ToHit) && ItemBridge.Potency(item.ToHit) is var p and not 0)
                parts.Add($"{p:+0;-0} potency");
            var rank = ItemBridge.Striking(item.ToDam) + (item.Modifier(ItemModifiers.Blows) > 0 && knowledge.KnowsRune(RuneIds.Modifier(ItemModifiers.Blows)) ? 1 : 0);
            if (knowledge.KnowsRune(RuneIds.ToDam) && rank > 0)
                parts.Add(rank switch { 1 => "striking", 2 => "greater striking", _ => "major striking" });
        }
        else if (bridge.ArmourFor(item) is { } armour)
        {
            parts.Add($"{armour.Category.ToString().ToLowerInvariant()}, AC {armour.AcBonus}");
            if (knowledge.KnowsRune(RuneIds.ToAc) && ItemBridge.Potency(item.ToAc) is var p and not 0)
                parts.Add($"{p:+0;-0} potency");
        }
        else if (bridge.ShieldFor(item) is { } shield)
        {
            parts.Add($"blocks {shield.Hardness}");
        }
        foreach (var brand in item.Brands.Where(b => knowledge.KnowsRune(RuneIds.Brand(b.Element))))
            parts.Add($"+1d{(brand.Multiplier >= 3 ? 6 : 4)} {Elements.GetValueOrDefault(brand.Element, brand.Element)}");
        foreach (var slay in item.Slays.Where(s => knowledge.KnowsRune(RuneIds.Slay(s.MonsterFlag))))
            parts.Add($"+{(slay.Multiplier >= 4 ? 2 : 1)}d6 against {slay.MonsterFlag.ToLowerInvariant()}");
        foreach (var resist in item.Resists.Where(r => knowledge.KnowsRune(RuneIds.Resist(r))))
            parts.Add(Elements.TryGetValue(resist, out var element) ? $"resist {element} {Gear.ResistRune}" : Protections.GetValueOrDefault(resist, resist));
        foreach (var (mod, value) in item.Modifiers.Where(m => m.Value != 0 && knowledge.KnowsRune(RuneIds.Modifier(m.Key))))
            parts.Add($"{value:+0;-0} {Modifier(mod)}");
        if (item.Flags.Contains(ItemFlags.Regen) && knowledge.KnowsRune(RuneIds.Flag(ItemFlags.Regen)))
            parts.Add("regeneration");
        // On other pieces Angband's to-hit, to-dam and to-AC count for a little (Gear: a sixth, a quarter, a tenth).
        if (bridge.WeaponFor(item) is null && bridge.ArmourFor(item) is null)
        {
            if (item.ToHit != 0 && knowledge.KnowsRune(RuneIds.ToHit))
                parts.Add($"{item.ToHit:+0;-0} accuracy");
            if (item.ToDam != 0 && knowledge.KnowsRune(RuneIds.ToDam))
                parts.Add($"{item.ToDam:+0;-0} damage");
            if (item.ToAc != 0 && knowledge.KnowsRune(RuneIds.ToAc))
                parts.Add($"{item.ToAc:+0;-0} armour");
        }

        var text = parts.Count > 0 ? $"{name} ({string.Join(", ", parts)})" : name;
        var notes = new List<string>();
        if (!knowledge.KnowsKind(item) && knowledge.HasTried(item.Kind))
            notes.Add("tried");
        if (knowledge.KnowsKind(item) && !knowledge.IsFullyKnown(item))
            notes.Add("??");
        return notes.Count > 0 ? $"{text} {{{string.Join(", ", notes)}}}" : text;
    }

    private static string Modifier(string mod) => mod switch
    {
        ItemModifiers.Speed => "speed",
        ItemModifiers.Stealth => "stealth",
        ItemModifiers.Light => "light",
        ItemModifiers.Strength => "Strength",
        ItemModifiers.Dexterity => "Dexterity",
        ItemModifiers.Constitution => "Constitution",
        ItemModifiers.Intelligence => "Intelligence",
        ItemModifiers.Wisdom => "Wisdom",
        ItemModifiers.Blows => "attacks",
        ItemModifiers.Infravision => "infravision",
        ItemModifiers.Searching => "searching",
        ItemModifiers.Tunnel => "digging",
        _ => mod,
    };
}
