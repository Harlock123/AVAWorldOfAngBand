using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldOfAngBand.Rules.Abilities;
using WorldOfAngBand.Rules.Items;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Content;

/// <summary>The rules' content: ancestries, backgrounds, classes, feats, abilities and items, read from the embedded JSON files.</summary>
public sealed class ContentDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<ContentDatabase> EmbeddedInstance = new(LoadEmbedded);
    public ContentDatabase(
        IEnumerable<AncestryDefinition> ancestries,
        IEnumerable<BackgroundDefinition> backgrounds,
        IEnumerable<ClassDefinition> classes,
        IEnumerable<AbilityDefinition>? abilities = null,
        IEnumerable<ItemDefinition>? items = null,
        IEnumerable<AffixDefinition>? affixes = null,
        IEnumerable<Characters.FeatDefinition>? feats = null,
        IEnumerable<SetDefinition>? sets = null)
    {
        Sets = (sets ?? []).ToDictionary(s => s.Id);
        Feats = (feats ?? []).ToDictionary(f => f.Id);
        Items = (items ?? []).ToDictionary(i => i.Id);
        Affixes = (affixes ?? []).ToList();
        Abilities = (abilities ?? []).ToDictionary(a => a.Id);
        Ancestries = ancestries.ToDictionary(a => a.Id);
        Backgrounds = backgrounds.ToDictionary(b => b.Id);
        Classes = classes.ToDictionary(c => c.Id);
    }

    /// <summary>The content that ships with the game, loaded once.</summary>
    public static ContentDatabase Embedded => EmbeddedInstance.Value;

    public IReadOnlyDictionary<string, AncestryDefinition> Ancestries { get; }
    public IReadOnlyDictionary<string, BackgroundDefinition> Backgrounds { get; }
    public IReadOnlyDictionary<string, ClassDefinition> Classes { get; }
    public IReadOnlyDictionary<string, AbilityDefinition> Abilities { get; }
    public IReadOnlyDictionary<string, ItemDefinition> Items { get; }
    public IReadOnlyList<AffixDefinition> Affixes { get; }
    public IReadOnlyDictionary<string, Characters.FeatDefinition> Feats { get; }
    public IReadOnlyDictionary<string, SetDefinition> Sets { get; }

    public ItemDefinition Item(string id) => Find(Items, id, "item");

    /// <summary>A loot roller over this content's items and affixes.</summary>
    public LootGenerator Loot => _loot ??= new LootGenerator(Items, Affixes);

    private LootGenerator? _loot;

    /// <summary>A class's starting ability bar: slot ("lmb", "rmb", "1".."6") to ability.</summary>
    public IReadOnlyDictionary<string, AbilityDefinition> AbilityBar(string classId) =>
        Abilities.Values.Where(a => a.Class == classId && !a.Feat).ToDictionary(a => a.Slot);

    public AncestryDefinition Ancestry(string id) => Find(Ancestries, id, "ancestry");
    public BackgroundDefinition Background(string id) => Find(Backgrounds, id, "background");
    public ClassDefinition Class(string id) => Find(Classes, id, "class");

    public static ContentDatabase LoadEmbedded() => new(
        Read<List<AncestryDefinition>>("ancestries.json"),
        Read<List<BackgroundDefinition>>("backgrounds.json"),
        Read<List<ClassDefinition>>("classes.json"),
        Read<List<AbilityDefinition>>("abilities.json"),
        Read<List<ItemDefinition>>("items.json"),
        Read<List<AffixDefinition>>("affixes.json"),
        Read<List<Characters.FeatDefinition>>("feats.json"),
        Read<List<SetDefinition>>("sets.json"));

    private static T Read<T>(string fileName)
    {
        var name = $"WorldOfAngBand.Content.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded content '{name}' is missing.");
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Embedded content '{name}' is empty.");
    }

    private static T Find<T>(IReadOnlyDictionary<string, T> table, string id, string kind) =>
        table.TryGetValue(id, out var value) ? value : throw new RulesException($"Unknown {kind} '{id}'.");
}
