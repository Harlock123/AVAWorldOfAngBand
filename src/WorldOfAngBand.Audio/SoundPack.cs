using System.Text.Json;

namespace WorldOfAngBand.Audio;

/// <summary>
/// A folder with a <c>soundpack.json</c> (AVABand's format, with ambience): effects keyed by event name, case and
/// all (Angband's message names such as HIT, OPENDOOR, BR_FIRE, and AshenReliquary's such as swing, gold, shrine),
/// music playlists keyed by mood (title, town, explore, dungeon, deep, combat, boss, epilogue) and ambience loops by
/// name. Each entry lists files, one picked at random. Paths are kept resolved, so a mod's pack can be laid over the
/// game's by key (<see cref="Over"/>). Anything missing is simply silent.
/// </summary>
public sealed class SoundPack
{
    public const string FileName = "soundpack.json";

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "audio");

    public string Name { get; init; } = "";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Sounds { get; init; } = Empty;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Music { get; init; } = Empty;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Ambience { get; init; } = Empty;

    /// <summary>Per-event volume scale (default 1).</summary>
    public IReadOnlyDictionary<string, float> Volumes { get; init; } = new Dictionary<string, float>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Empty = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>A pack with nothing in it: the game is silent.</summary>
    public static SoundPack None { get; } = new();

    public float Volume(string name) => Volumes.TryGetValue(name, out var v) ? v : 1f;

    /// <summary>This pack with <paramref name="mod"/>'s entries in place of its own, key by key.</summary>
    public SoundPack Over(SoundPack mod) => new()
    {
        Name = string.IsNullOrEmpty(Name) ? mod.Name : Name,
        Sounds = Merge(Sounds, mod.Sounds),
        Music = Merge(Music, mod.Music),
        Ambience = Merge(Ambience, mod.Ambience),
        Volumes = new[] { Volumes, mod.Volumes }.SelectMany(d => d).GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal),
    };

    private static Dictionary<string, IReadOnlyList<string>> Merge(IReadOnlyDictionary<string, IReadOnlyList<string>> under,
        IReadOnlyDictionary<string, IReadOnlyList<string>> over)
    {
        var merged = new Dictionary<string, IReadOnlyList<string>>(under, StringComparer.Ordinal);
        foreach (var (key, files) in over)
            merged[key] = files;
        return merged;
    }

    /// <summary>Every file the pack plays.</summary>
    public IEnumerable<string> ReferencedFiles() =>
        Sounds.Values.Concat(Music.Values).Concat(Ambience.Values).SelectMany(f => f).Distinct(StringComparer.Ordinal);

    private sealed class Json
    {
        public string? Name { get; init; }
        public Dictionary<string, List<string>>? Sounds { get; init; }
        public Dictionary<string, List<string>>? Music { get; init; }
        public Dictionary<string, List<string>>? Ambience { get; init; }
        public Dictionary<string, float>? Volumes { get; init; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads and checks a pack; throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static SoundPack Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        Json json;
        try
        {
            json = JsonSerializer.Deserialize<Json>(File.ReadAllText(path), Options) ?? new Json();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }

        var errors = new List<string>();
        IReadOnlyDictionary<string, IReadOnlyList<string>> Lists(Dictionary<string, List<string>>? d) =>
            (d ?? []).ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Select(Resolve).ToList(), StringComparer.Ordinal);
        string Resolve(string file)
        {
            if (file.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(file))
                errors.Add($"'{file}' must be a relative path inside the pack.");
            else if (!AudioDecoders.Extensions.Contains(Path.GetExtension(file)))
                errors.Add($"'{file}' is not .wav, .ogg or .mp3.");
            var full = Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar));
            if (errors.Count == 0 && !File.Exists(full))
                errors.Add($"missing file '{file}'.");
            return full;
        }

        var pack = new SoundPack
        {
            Name = json.Name ?? Path.GetFileName(directory),
            Sounds = Lists(json.Sounds),
            Music = Lists(json.Music),
            Ambience = Lists(json.Ambience),
            Volumes = new Dictionary<string, float>(json.Volumes ?? [], StringComparer.Ordinal),
        };
        if (errors.Count > 0)
            throw new InvalidDataException($"Sound pack '{directory}' is invalid:{Environment.NewLine}" +
                                           string.Join(Environment.NewLine, errors.Distinct().Select(e => "  - " + e)));
        return pack;
    }

    /// <summary>
    /// The game's pack with each mod's <c>audio/soundpack.json</c> laid over it, in mod order. A broken pack is left
    /// out and its problem added to <paramref name="problems"/>; with no game pack at all, the game is silent.
    /// </summary>
    public static SoundPack LoadWithMods(string directory, IEnumerable<string> modDirectories, List<string>? problems = null)
    {
        var pack = None;
        foreach (var dir in new[] { directory }.Concat(modDirectories.Select(m => Path.Combine(m, "audio"))))
        {
            if (!File.Exists(Path.Combine(dir, FileName)))
                continue;
            try
            {
                pack = pack.Over(Load(dir));
            }
            catch (InvalidDataException ex)
            {
                problems?.Add(ex.Message);
            }
        }
        return pack;
    }
}
