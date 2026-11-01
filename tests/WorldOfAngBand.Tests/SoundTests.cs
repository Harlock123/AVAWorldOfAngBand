using System.Text.Json;
using WorldOfAngBand.Audio;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Province;

namespace WorldOfAngBand.Tests;

public class SoundTests
{
    /// <summary>The game's sound pack, in the source tree (the app copies it beside the executable).</summary>
    private static readonly Lazy<string> PackDirectory = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var audio = Path.Combine(dir.FullName, "src", "WorldOfAngBand.App", "audio");
            if (File.Exists(Path.Combine(audio, SoundPack.FileName)))
                return audio;
        }
        throw new DirectoryNotFoundException("src/WorldOfAngBand.App/audio");
    });

    private static readonly Lazy<SoundPack> Pack = new(() => SoundPack.Load(PackDirectory.Value));

    /// <summary>Hears everything and plays nothing: what started, what stopped, which effects.</summary>
    private sealed class Recorder : NullAudioEngine
    {
        public override bool IsAvailable => true;
        public List<string> Tracks { get; } = [];
        public List<(string File, float Gain, float Pan)> Effects { get; } = [];
        public float Master { get; private set; } = 1;

        public override void PlayMusic(string path, bool loop = true)
        {
            base.PlayMusic(path, loop);
            Tracks.Add(path);
        }

        public override void PlayEffect(string path, float gain = 1f, float pan = 0f) => Effects.Add((path, gain, pan));

        public override void SetVolumes(float master, float effects, float music) => Master = master;
    }

    private static (SoundDirector Sound, Recorder Engine) Director()
    {
        var engine = new Recorder();
        return (new SoundDirector(engine, Pack.Value), engine);
    }

    [Fact]
    public void The_sound_pack_loads_and_credits_every_file_it_ships()
    {
        var pack = Pack.Value;
        Assert.NotEmpty(pack.Sounds);
        var listing = JsonDocument.Parse(File.ReadAllText(Path.Combine(PackDirectory.Value, SoundPack.FileName))).RootElement;
        var credited = listing.GetProperty("files").EnumerateArray().ToDictionary(f => f.GetProperty("file").GetString()!, f => f);
        var shipped = Directory.EnumerateFiles(PackDirectory.Value, "*", SearchOption.AllDirectories)
            .Where(f => AudioDecoders.Extensions.Contains(Path.GetExtension(f)))
            .Select(f => Path.GetRelativePath(PackDirectory.Value, f).Replace('\\', '/'))
            .ToList();
        foreach (var file in shipped)
        {
            Assert.True(credited.TryGetValue(file, out var credit), $"{file} has no credit");
            Assert.False(string.IsNullOrWhiteSpace(credit.GetProperty("licence").GetString()), file);
            Assert.False(string.IsNullOrWhiteSpace(credit.GetProperty("author").GetString()), file);
        }
        // Every file the pack plays ships, and every file shipped is played.
        var played = pack.ReferencedFiles().Select(f => Path.GetRelativePath(PackDirectory.Value, f).Replace('\\', '/')).ToHashSet();
        Assert.Equal(played.Order(), shipped.Order());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("town")]
    [InlineData("explore")]
    [InlineData("dungeon")]
    [InlineData("deep")]
    [InlineData("combat")]
    [InlineData("boss")]
    [InlineData("epilogue")]
    public void Every_mood_has_music(string mood) => Assert.NotEmpty(Pack.Value.Music[mood]);

    [Fact]
    public void Every_place_has_its_ambience_in_the_pack()
    {
        var game = new DungeonSession(TestData.Data, 801);
        foreach (var zone in ProvinceMap.Shared.Zones.Where(z => z.Id != ProvinceMap.Gallowmere))
        {
            game.EnterZone(zone.Id, null);
            Assert.True(Pack.Value.Ambience.ContainsKey(Soundscape.Ambience(game)[0]), zone.Id);
        }
        foreach (var profile in new[] { "classic", "cavern", "labyrinth", "moria", "lair", "gauntlet", "hard_centre", "modified" })
            foreach (var depth in new[] { 1, 40, 80 })
                Assert.Contains(Soundscape.Dungeon(depth, profile), name => Pack.Value.Ambience.ContainsKey(name));
    }

    [Fact]
    public void The_music_follows_the_hero_from_the_town_to_the_province_and_down_the_shaft()
    {
        var game = new DungeonSession(TestData.Data, 802);
        Assert.Equal("town", Soundscape.PlaceMood(game));
        Assert.Equal("town-day", Soundscape.Ambience(game)[0]);
        game.EnterZone("gallows_road", null);
        Assert.Equal("explore", Soundscape.PlaceMood(game));
        Assert.Equal("town-day", Soundscape.Ambience(game)[0]);
        game.EnterZone("rotwater_fens", null);
        Assert.Equal("town-night", Soundscape.Ambience(game)[0]);
        game.EnterZone("drowned_chapel", null);
        Assert.Equal("dungeon", Soundscape.PlaceMood(game));
        Assert.Equal("dungeon-shallow", Soundscape.Ambience(game)[0]);
        game.EnterZone("ironhold", null);
        Assert.Equal("town", Soundscape.PlaceMood(game));
        Assert.Equal("fortress", Soundscape.Ambience(game)[0]);
        game.EnterZone("frostbitten_pass", null);
        Assert.Equal("labyrinth", Soundscape.Ambience(game)[0]);
        game.EnterZone("reliquary", null);
        Assert.Equal("lair", Soundscape.Ambience(game)[0]);
        game.JumpTo(12);
        Assert.Equal("dungeon", Soundscape.PlaceMood(game));
        game.JumpTo(45);
        Assert.Equal("deep", Soundscape.PlaceMood(game));
        Assert.Contains("dungeon-deep", Soundscape.Ambience(game));
        game.JumpTo(75);
        Assert.Contains("dungeon-abyss", Soundscape.Ambience(game));
        Assert.Equal("title", Soundscape.Mood(game, SoundScreen.Title));
        Assert.Equal("epilogue", Soundscape.Mood(game, SoundScreen.Epilogue));
        Assert.Empty(Soundscape.Ambience(game, SoundScreen.Title));
    }

    [Fact]
    public void The_director_plays_a_mood_a_track_at_a_time_and_changes_with_the_place()
    {
        var (sound, engine) = Director();
        var game = new DungeonSession(TestData.Data, 803);
        sound.Update(game, SoundScreen.Title);
        Assert.Equal("title", sound.Mood);
        Assert.EndsWith("title_theme.ogg", engine.CurrentMusic);
        Assert.Null(engine.CurrentAmbience);
        sound.Update(game);
        Assert.Equal("town", sound.Mood);
        Assert.Contains(engine.CurrentMusic, Pack.Value.Music["town"]);
        Assert.EndsWith("ambient-town-day.ogg", engine.CurrentAmbience);
        // Each frame asks again; nothing restarts while the place is the same.
        var tracks = engine.Tracks.Count;
        for (var i = 0; i < 10; i++)
            sound.Update(game);
        Assert.Equal(tracks, engine.Tracks.Count);
        // A track that plays out is followed by another of the mood, not the same one again.
        var last = engine.CurrentMusic;
        engine.FinishMusic();
        Assert.Contains(engine.CurrentMusic, Pack.Value.Music["town"]);
        Assert.NotEqual(last, engine.CurrentMusic);
        game.JumpTo(50);
        sound.Update(game);
        Assert.Equal("deep", sound.Mood);
        Assert.Contains(engine.CurrentMusic, Pack.Value.Music["deep"]);
    }

    [Fact]
    public void Turned_off_or_muted_nothing_plays_and_turned_on_it_comes_back()
    {
        var (sound, engine) = Director();
        var game = new DungeonSession(TestData.Data, 804);
        sound.Update(game);
        Assert.NotNull(engine.CurrentMusic);
        sound.Apply(new SoundSettings { MusicOn = false, AmbienceOn = false });
        sound.Update(game);
        Assert.Null(engine.CurrentMusic);
        Assert.Null(engine.CurrentAmbience);
        sound.Apply(new SoundSettings { Muted = true });
        sound.Update(game);
        sound.Play("swing");
        Assert.Null(engine.CurrentMusic);
        Assert.Empty(engine.Effects);
        Assert.Equal(0, engine.Master);
        sound.Apply(new SoundSettings());
        sound.Update(game);
        sound.Play("swing");
        Assert.NotNull(engine.CurrentMusic);
        Assert.Single(engine.Effects);
        sound.Play("no such sound");
        Assert.Single(engine.Effects);
    }

    [Fact]
    public void A_mods_sound_pack_lays_its_own_over_the_games_by_key()
    {
        var mod = Path.Combine(Path.GetTempPath(), "worldofangband-tests", Guid.NewGuid().ToString("N"));
        var audio = Path.Combine(mod, "audio");
        Directory.CreateDirectory(audio);
        File.Copy(Pack.Value.Sounds["swing"][0], Path.Combine(audio, "whoosh.ogg"));
        File.WriteAllText(Path.Combine(audio, SoundPack.FileName), """{ "sounds": { "swing": ["whoosh.ogg"] }, "music": { "town": ["whoosh.ogg"] } }""");
        var broken = Path.Combine(Path.GetTempPath(), "worldofangband-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(broken, "audio"));
        File.WriteAllText(Path.Combine(broken, "audio", SoundPack.FileName), """{ "sounds": { "hit": ["missing.ogg"] } }""");
        try
        {
            var problems = new List<string>();
            var pack = SoundPack.LoadWithMods(PackDirectory.Value, [mod, broken], problems);
            Assert.Equal([Path.Combine(audio, "whoosh.ogg")], pack.Sounds["swing"]);
            Assert.Equal([Path.Combine(audio, "whoosh.ogg")], pack.Music["town"]);
            Assert.Equal(Pack.Value.Sounds["hit"], pack.Sounds["hit"]);
            Assert.Equal(Pack.Value.Music["deep"], pack.Music["deep"]);
            Assert.Contains(problems, p => p.Contains("missing.ogg"));
        }
        finally
        {
            Directory.Delete(mod, true);
            Directory.Delete(broken, true);
        }
    }

    [Fact]
    public void With_no_pack_the_game_is_silent_and_says_nothing_is_wrong()
    {
        var pack = SoundPack.LoadWithMods(Path.Combine(Path.GetTempPath(), "no-such-pack"), []);
        var engine = new Recorder();
        var sound = new SoundDirector(engine, pack);
        sound.Update(new DungeonSession(TestData.Data, 805));
        sound.Play("swing");
        Assert.Null(engine.CurrentMusic);
        Assert.Null(engine.CurrentAmbience);
        Assert.Empty(engine.Effects);
    }

    [Theory]
    [InlineData(AudioBuffer.Automatic, false, 0)]
    [InlineData(AudioBuffer.Automatic, true, 4096)]
    [InlineData(AudioBuffer.Large, false, 2048)]
    [InlineData(AudioBuffer.ExtraLarge, false, 4096)]
    public void The_mixing_period_is_extra_large_in_a_virtual_machine(string choice, bool vm, int frames) =>
        Assert.Equal(frames, AudioBuffer.PeriodFrames(choice, vm));
}
