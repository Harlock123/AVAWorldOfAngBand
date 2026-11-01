using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Items;
using WorldOfAngBand.Rules.Rules;
using AngbandItem = WorldOfAngBand.Engine.Items.Item;
using EquipSlot = WorldOfAngBand.Engine.Definitions.EquipSlot;

namespace WorldOfAngBand.Game;

/// <summary>An extra die of damage a weapon's rune adds: of an element (a brand), or against a kind of monster (a slay).</summary>
public sealed record ExtraDamage(string Rune, int Dice, int Sides, DamageType Type, string? MonsterFlag = null);

/// <summary>
/// What the hero's worn Angband items come to in Pathfinder's terms (the spec's section 6.3), worked out again each time
/// the equipment or the hero's temporary effects change:
/// <list type="bullet">
/// <item>the weapon: its bridged die and traits, a potency rune from its to-hit and striking from its to-dam (and an
/// extra-attacks ego); its brands and slays as extra dice;</item>
/// <item>the body armour: its category and AC, potency from its to-AC;</item>
/// <item>the rest: resistances, protections (free action, fear, blindness…), and Angband's modifiers — speed and stealth
/// on Angband's own scales, light radius, Strength as damage, Dexterity and Wisdom as Reflex and Will, Constitution as
/// hit points; to-hit, to-dam and to-AC on other pieces as small untyped bonuses.</item>
/// </list>
/// </summary>
public sealed class Gear
{
    /// <summary>The resistance a resist rune gives (Pathfinder's energy-resistant rune).</summary>
    public const int ResistRune = 5;

    private static readonly Dictionary<string, DamageType> Elements = new()
    {
        ["fire"] = DamageType.Fire, ["cold"] = DamageType.Cold, ["acid"] = DamageType.Acid, ["elec"] = DamageType.Electricity,
        ["pois"] = DamageType.Poison,
    };

    public int Speed { get; private set; }
    public int Stealth { get; private set; }
    public int Light { get; private set; }
    public bool Regenerate { get; private set; }
    public IReadOnlySet<string> Protections { get; private set; } = new HashSet<string>();
    public IReadOnlyList<ExtraDamage> Extras { get; private set; } = [];

    /// <summary>The weapon (if any) whose numbers are in use.</summary>
    public AngbandItem? Weapon { get; private set; }

    /// <summary>The Angband protection id (free_act, fear, blind, conf, sust_str, hold_life…) is worn.</summary>
    public bool Protects(string protection) => Protections.Contains(protection);

    /// <summary>Works out the hero's gear and sets it on their character.</summary>
    public void Apply(Hero hero, ItemBridge bridge)
    {
        var c = hero.Character;
        var worn = hero.Pack.Equipped.ToList();
        var weapon = hero.Pack.Weapon;
        Weapon = weapon;

        // The weapon.
        if (weapon is not null && bridge.WeaponFor(weapon) is { } definition)
        {
            var striking = Math.Min(3, ItemBridge.Striking(weapon.ToDam) + (weapon.Modifier(ItemModifiers.Blows) > 0 ? 1 : 0));
            c.Weapon = new Weapon(definition, ItemBridge.Potency(weapon.ToHit), striking);
        }
        else
        {
            c.Weapon = new Weapon(Character.Fist);
        }
        // The body armour.
        var body = worn.FirstOrDefault(i => i.Base.Slot == EquipSlot.Body);
        c.Armor = body is not null && bridge.ArmourFor(body) is { } armour
            ? new Armor(armour, ItemBridge.Potency(body.ToAc))
            : Armor.None;

        var resist = new Dictionary<DamageType, int>();
        var protections = new HashSet<string>();
        var extras = new List<ExtraDamage>();
        int speed = 0, stealth = 0, light = 0, damage = 0, reflex = 0, will = 0, hp = 0, otherHit = 0, otherDam = 0, otherAc = 0;
        var regenerate = false;
        foreach (var item in worn)
        {
            foreach (var r in item.Resists)
            {
                if (Elements.TryGetValue(r, out var type))
                    resist[type] = Math.Max(resist.GetValueOrDefault(type), ResistRune);
                else
                    protections.Add(r);
            }
            regenerate |= item.Flags.Contains(ItemFlags.Regen);
            if (item.Flags.Contains(ItemFlags.HoldLife))
                protections.Add("hold_life");
            speed += item.Modifier(ItemModifiers.Speed);
            stealth += item.Modifier(ItemModifiers.Stealth);
            light += item.Modifier(ItemModifiers.Light);
            damage += (item.Modifier(ItemModifiers.Strength) + 1) / 2;
            reflex += Math.Clamp(item.Modifier(ItemModifiers.Dexterity), -3, 3);
            will += Math.Clamp(item.Modifier(ItemModifiers.Wisdom), -3, 3);
            hp += item.Modifier(ItemModifiers.Constitution) * c.Level;
            if (item != weapon && item != body)
            {
                otherHit += item.ToHit;
                otherDam += item.ToDam;
                otherAc += item.ToAc;
            }
            if (item == weapon)
            {
                foreach (var brand in item.Brands)
                    if (Elements.TryGetValue(brand.Element, out var type))
                        extras.Add(new ExtraDamage(RuneIds.Brand(brand.Element), 1, brand.Multiplier >= 3 ? 6 : 4, type));
                foreach (var slay in item.Slays)
                    extras.Add(new ExtraDamage(RuneIds.Slay(slay.MonsterFlag), slay.Multiplier >= 4 ? 2 : 1, 6, c.Weapon.Definition.DamageType, slay.MonsterFlag));
            }
        }
        // Temporary effects: a potion's resistance, heroism, berserking, a blessing.
        foreach (var (effect, _) in hero.Timed)
        {
            if (effect.StartsWith("resist:", StringComparison.Ordinal) && Enum.TryParse<DamageType>(effect[7..], out var type))
                resist[type] = Math.Max(resist.GetValueOrDefault(type), 10);
            if (effect is "heroism" or "berserk")
                protections.Add("fear");
        }

        c.ApplyGear(new GearBonuses
        {
            MaxHitPoints = hp,
            WeaponDamage = damage + otherDam / 4,
            Resistances = resist,
            Saves = new Dictionary<StatisticKind, int> { [StatisticKind.Reflex] = reflex, [StatisticKind.Will] = will },
        });
        // Bonuses from other pieces, and from what the hero has drunk or read: small, untyped or status (re-added each
        // time, since ApplyGear takes the gear's modifiers away).
        if (otherHit / 6 != 0)
            c.AddModifier(StatisticKind.Attack, new Modifier(Character.GearSource, otherHit / 6));
        if (otherAc / 10 != 0)
            c.AddModifier(StatisticKind.ArmorClass, new Modifier(Character.GearSource, otherAc / 10));
        var status = (hero.Timed.ContainsKey("heroism") ? 1 : 0) + (hero.Timed.ContainsKey("berserk") ? 2 : 0) + (hero.Timed.ContainsKey("bless") ? 1 : 0);
        if (status > 0)
            c.AddModifier(StatisticKind.Attack, new Modifier(Character.GearSource, Math.Min(2, status), BonusType.Status));
        if (hero.Timed.ContainsKey("bless"))
            c.AddModifier(StatisticKind.ArmorClass, new Modifier(Character.GearSource, 1, BonusType.Status));
        if (hero.Timed.ContainsKey("berserk"))
            c.AddModifier(StatisticKind.ArmorClass, new Modifier(Character.GearSource, -1, BonusType.Status));

        Speed = speed;
        Stealth = stealth;
        Light = light;
        Regenerate = regenerate;
        Protections = protections;
        Extras = extras;
    }
}
