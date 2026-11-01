using System.Text.Json;
using System.Text.Json.Serialization;
using WorldOfAngBand.Data;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Items;
using AngbandItem = WorldOfAngBand.Engine.Items.Item;

namespace WorldOfAngBand.Game.Bridge;

/// <summary>A melee weapon in Pathfinder's terms: its die, damage type, traits and paper-doll layer.</summary>
public sealed record BridgedWeapon(int Die, DamageType Type, string Gfx, IReadOnlyList<WeaponTrait>? Traits = null, int? Deadly = null);

/// <summary>Body armour in Pathfinder's terms, with its paper-doll layers.</summary>
public sealed record BridgedArmour(ArmorCategory Category, int Ac, int DexCap, string Gfx, string Legs, int CheckPenalty = 0, int Strength = 0);

/// <summary>A shield: what Shield Block takes off a blow, and its paper-doll layer.</summary>
public sealed record BridgedShield(int Hardness, string Gfx);

/// <summary>
/// The bridge from Angband's items to the game (data/bridge/items.json, the spec's section 6.3): which kinds and egos
/// may be made, what a melee weapon or body armour is in Pathfinder's terms, what a potion or scroll does, and the
/// Ashen setting's names for egos. The items themselves stay Angband's: the hero's Pathfinder numbers are worked out
/// from them (<see cref="Gear"/>).
/// </summary>
public sealed class ItemBridge
{
    private readonly File _file;
    private readonly Dictionary<string, WeaponDefinition> _weapons = [];
    private readonly Dictionary<string, ArmorDefinition> _armour = [];

    private ItemBridge(GameData data, File file)
    {
        _file = file;
        Data = data;
        foreach (var id in file.Weapons.Keys.Concat(file.Armour.Keys).Concat(file.Shields.Keys).Concat(file.Worn.Keys).Concat(file.Use.Keys))
            if (data.Objects.All(k => k.Id != id))
                throw new GameDataException($"The item bridge names unknown object '{id}'.");
        foreach (var id in file.Egos.Concat(file.EgoNames.Keys))
            if (data.Egos.All(e => e.Id != id))
                throw new GameDataException($"The item bridge names unknown ego '{id}'.");
        foreach (var (id, w) in file.Weapons)
        {
            var name = Engine.Items.ItemNaming.Plain(data.Objects.First(k => k.Id == id).Name, false);
            var traits = (w.Traits ?? []).ToHashSet();
            _weapons[id] = new WeaponDefinition(id, name, w.Die, w.Type, 1.0, false, traits, w.Deadly)
            {
                VersatileType = traits.Contains(WeaponTrait.Versatile)
                    ? w.Type == DamageType.Slashing ? DamageType.Piercing : w.Type == DamageType.Piercing ? DamageType.Slashing : DamageType.Piercing
                    : null,
            };
        }
        foreach (var (id, a) in file.Armour)
            _armour[id] = new ArmorDefinition(id, Engine.Items.ItemNaming.Plain(data.Objects.First(k => k.Id == id).Name, false), a.Category, a.Ac, a.DexCap, a.CheckPenalty, 0, a.Strength);
    }

    public GameData Data { get; }

    public static ItemBridge Load(GameData data, string? dataDirectory = null)
    {
        var path = Path.Combine(dataDirectory ?? DataLoader.DefaultDataDirectory, "bridge", "items.json");
        using var stream = System.IO.File.OpenRead(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var file = JsonSerializer.Deserialize<File>(stream, options) ?? throw new GameDataException($"{path} is empty.");
        return new ItemBridge(data, file);
    }

    private sealed record File(
        Dictionary<string, BridgedWeapon> Weapons,
        Dictionary<string, BridgedArmour> Armour,
        Dictionary<string, BridgedShield> Shields,
        Dictionary<string, string?> Worn,
        Dictionary<string, string> Use,
        List<string> Egos,
        Dictionary<string, string> EgoNames);

    /// <summary>Whether a kind is in the game yet (gold always is).</summary>
    public bool Has(ObjectKindDef kind) =>
        kind.Base == "gold" || _file.Weapons.ContainsKey(kind.Id) || _file.Armour.ContainsKey(kind.Id) || _file.Shields.ContainsKey(kind.Id)
        || _file.Worn.ContainsKey(kind.Id) || _file.Use.ContainsKey(kind.Id);

    public bool Allows(EgoItemDef ego) => _file.Egos.Contains(ego.Id);

    /// <summary>The Egos brought in so far.</summary>
    public IReadOnlyList<string> Egos => _file.Egos;

    public string EgoName(EgoItemDef ego) => _file.EgoNames.GetValueOrDefault(ego.Id, ego.Name);

    public WeaponDefinition? WeaponFor(AngbandItem item) => _weapons.GetValueOrDefault(item.Kind.Id);

    public ArmorDefinition? ArmourFor(AngbandItem item) => _armour.GetValueOrDefault(item.Kind.Id);

    public BridgedShield? ShieldFor(AngbandItem item) => _file.Shields.GetValueOrDefault(item.Kind.Id);

    /// <summary>What a potion or scroll does (data/bridge/items.json's "use"), or null if it can't be used.</summary>
    public string? UseOf(ObjectKindDef kind) => _file.Use.GetValueOrDefault(kind.Id);

    /// <summary>The paper-doll layer an item shows as, or null.</summary>
    public string? GfxFor(AngbandItem item) =>
        _file.Weapons.TryGetValue(item.Kind.Id, out var w) ? w.Gfx
        : _file.Armour.TryGetValue(item.Kind.Id, out var a) ? a.Gfx
        : _file.Shields.TryGetValue(item.Kind.Id, out var s) ? s.Gfx
        : _file.Worn.GetValueOrDefault(item.Kind.Id);

    /// <summary>The legs body armour shows with (Angband has no leg armour; the body armour's kind dresses them).</summary>
    public string? LegsFor(AngbandItem item) => _file.Armour.TryGetValue(item.Kind.Id, out var a) ? a.Legs : null;

    /// <summary>Pathfinder's potency rune for an Angband to-hit or to-AC: +1 for 3 and up, +2 for 8, +3 for 13 (and less below 0).</summary>
    public static int Potency(int bonus) => Math.Sign(bonus) * (Math.Abs(bonus) >= 13 ? 3 : Math.Abs(bonus) >= 8 ? 2 : Math.Abs(bonus) >= 3 ? 1 : 0);

    /// <summary>Pathfinder's striking rune for an Angband to-dam: striking at 4, greater at 10, major at 16.</summary>
    public static int Striking(int bonus) => bonus >= 16 ? 3 : bonus >= 10 ? 2 : bonus >= 4 ? 1 : 0;

    /// <summary>The Angband bonus that gives a potency rank (the runesmith etches up to it).</summary>
    public static int PotencyThreshold(int rank) => rank switch { 1 => 3, 2 => 8, _ => 13 };

    /// <summary>The Angband to-dam that gives a striking rank.</summary>
    public static int StrikingThreshold(int rank) => rank switch { 1 => 4, 2 => 10, _ => 16 };
}
