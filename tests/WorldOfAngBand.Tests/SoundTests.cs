using System.Text.Json;
using WorldOfAngBand.Audio;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Province;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

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

    /// <summary>A director following a game on the first level, with nothing else on it and the hero's square lit.</summary>
    private static (DungeonSession Game, SoundDirector Sound, List<string> Heard) Below(ulong seed)
    {
        var game = new DungeonSession(TestData.Data, seed);
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        game.LightArea(game.Hero.Position, 8);
        game.Perform(new Hold());
        var (sound, _) = Director();
        var heard = new List<string>();
        sound.Played += (name, _, _) => heard.Add(name);
        sound.Update(game);
        return (game, sound, heard);
    }

    private static Foe Beside(DungeonSession game, string race, int distance = 1)
    {
        var spot = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.ChebyshevTo(game.Hero.Position) == distance
                && Engine.Combat.ProjectionPath.Projectable(game.Level, p, game.Hero.Position, 20))
            .First();
        game.Spawn(TestData.Data.Monster(race)!, spot, asleep: false);
        var foe = game.FoeAt(spot)!;
        foe.Monster.MaxHp = foe.Monster.Hp = 500;
        return foe;
    }

    [Fact]
    public void Every_sound_the_game_can_ask_for_is_in_the_pack()
    {
        var pack = Pack.Value;
        var names = new HashSet<string>
        {
            "swing", "hit", "crit", "block", "hero-hurt", "HITPOINT_WARN", "RECOVER", "ready", "footstep", "STORE_HOME",
            "STORE_ENTER", "inventory", "level-up", "INVULN", "death", "KILL", "KILL_UNIQUE", "KILL_KING",
        };
        foreach (var method in TestData.Data.BlowMethods)
            names.Add(SoundCues.Strike(new StrikeEvent(default, default, false, DegreeOfSuccess.Success, 1, method.Id))!);
        foreach (var spell in TestData.Data.MonsterSpells)
            names.Add(SoundCues.Spell(spell.Id)!);
        names.Add(SoundCues.Spell("ASHEN_SHOOT")!);
        names.Add(SoundCues.Spell("ASHEN_CAST")!);
        foreach (var cue in Enum.GetValues<Cue>())
            foreach (var item in new[] { null, "sword", "soft_armour", "ring", "potion", "scroll" })
                if (SoundCues.Cue(new CueEvent(cue, null, item)) is { } name)
                    names.Add(name);
        foreach (var kind in Enum.GetValues<Rules.Abilities.AbilityKind>())
            foreach (var type in Enum.GetValues<DamageType>())
                if (SoundCues.Ability(new AbilityUsedEvent("x", kind.ToString(), type.ToString())) is { } name)
                    names.Add(name);
        foreach (var kind in Enum.GetValues<ConditionKind>())
            if (SoundCues.ConditionStarts(kind) is { } name)
                names.Add(name);
        foreach (var effect in new[] { "haste", "heroism", "berserk", "bless" })
            names.Add(SoundCues.TimedStarts(effect)!);
        foreach (var race in TestData.Data.Monsters)
        {
            var ashen = race.Id.StartsWith(AshenCreature.RacePrefix, StringComparison.Ordinal)
                ? ProvinceMap.Shared.Creature(race.Id[AshenCreature.RacePrefix.Length..])?.Voice
                : null;
            if (SoundCues.Voice(race, ashen) is { } voice)
            {
                names.Add(voice);
                names.Add(voice + "-death");
            }
        }
        Assert.Empty(names.Where(n => !pack.Sounds.ContainsKey(n)).Order());
    }

    [Fact]
    public void Ashens_creatures_speak_with_their_own_voices_and_angbands_by_their_kind()
    {
        Assert.Equal("skeleton", SoundCues.Voice(TestData.Data.Monster("ashen_bone_archer")!, ProvinceMap.Shared.Creature("bone_archer")?.Voice));
        Assert.Equal("wolf", SoundCues.Voice(TestData.Data.Monster("jackal")!));
        Assert.Equal("crawler", SoundCues.Voice(TestData.Data.Monster("giant_white_centipede")!));
        Assert.Null(SoundCues.Voice(TestData.Data.Monster("kobold")!));
    }

    [Fact]
    public void A_fight_sounds_the_heros_strikes_the_monsters_blows_and_the_kill()
    {
        var (game, _, heard) = Below(811);
        var jackal = Beside(game, "jackal");
        for (var i = 0; i < 12; i++)
            game.Perform(new Walk(DirectionExtensions.FromOffset(jackal.Monster.Position.X - game.Hero.Position.X, jackal.Monster.Position.Y - game.Hero.Position.Y)));
        Assert.Contains(heard, n => n is "hit" or "crit");
        Assert.Contains(heard, n => n is "swing" or "MON_BITE");
        jackal.Monster.Hp = 1;
        while (jackal.Monster.IsActive && !jackal.Monster.IsRemoved && !game.IsOver)
            game.Perform(new Walk(DirectionExtensions.FromOffset(jackal.Monster.Position.X - game.Hero.Position.X, jackal.Monster.Position.Y - game.Hero.Position.Y)));
        Assert.Contains("wolf-death", heard);
    }

    [Fact]
    public void Being_hurt_and_coming_to_harm_have_their_sounds()
    {
        var (game, sound, heard) = Below(812);
        game.HurtHero(game.Hero.MaxHitPoints * 4 / 5, "a test");
        sound.Update(game);
        Assert.Contains("hero-hurt", heard);
        Assert.Contains("HITPOINT_WARN", heard);
        game.Hero.Character.Conditions.Apply(ConditionKind.Frightened, 1, 30);
        sound.Update(game);
        Assert.Contains("AFRAID", heard);
    }

    [Fact]
    public void The_stairs_doors_and_the_town_have_their_sounds_and_footsteps_only_outdoors()
    {
        var game = new DungeonSession(TestData.Data, 813);
        var (sound, _) = Director();
        var heard = new List<string>();
        sound.Played += (name, _, _) => heard.Add(name);
        sound.Update(game);
        var step = Enum.GetValues<Direction>().First(d => d is not (Direction.None or Direction.Here) && game.Level.IsPassable(game.Hero.Position.Step(d)) && game.FoeAt(game.Hero.Position.Step(d)) is null);
        game.Perform(new Walk(step));
        Assert.Contains("footstep", heard);
        game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Perform(new TakeStairs(Down: true));
        Assert.Contains("STAIRS_DOWN", heard);
        heard.Clear();
        var below = Enum.GetValues<Direction>().First(d => d is not (Direction.None or Direction.Here) && game.Level.IsPassable(game.Hero.Position.Step(d)) && game.FoeAt(game.Hero.Position.Step(d)) is null);
        game.Perform(new Walk(below));
        Assert.DoesNotContain("footstep", heard);
    }

    [Fact]
    public void A_hunter_in_view_brings_the_fights_music_and_a_unique_the_bosss()
    {
        var (game, sound, _) = Below(814);
        Assert.Equal("dungeon", sound.Mood);
        Beside(game, "jackal", 3);
        game.Perform(new Hold());
        sound.Update(game);
        Assert.Equal("combat", sound.Mood);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        sound.Update(game);
        Assert.Equal("combat", sound.Mood);
        for (var i = 0; i < 20; i++)
            game.Perform(new Hold());
        sound.Update(game);
        Assert.Equal("dungeon", sound.Mood);
        var unique = TestData.Data.Monsters.First(m => m.IsUnique && game.Bridge.Has(m) && m.Depth < 10);
        var spot = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p) && p.ChebyshevTo(game.Hero.Position) == 2);
        game.Spawn(unique, spot, asleep: false);
        game.Perform(new Hold());
        sound.Update(game);
        Assert.Equal("boss", sound.Mood);
    }

    [Fact]
    public void A_sound_is_placed_on_screen_and_quieter_far_away()
    {
        var (_, sound, _) = Below(815);
        // Isometric: east is up and right on screen, north is up and left.
        Assert.True(sound.Placement(new Loc(3, 0)).Pan > 0);
        Assert.True(sound.Placement(new Loc(0, -3)).Pan > 0);
        Assert.True(sound.Placement(new Loc(-3, 0)).Pan < 0);
        Assert.Equal(0f, sound.Placement(new Loc(3, 3)).Pan);
        Assert.True(sound.Placement(new Loc(15, 0)).Gain < sound.Placement(new Loc(1, 0)).Gain);
        sound.Isometric = false;
        Assert.Equal(0f, sound.Placement(new Loc(0, -3)).Pan);
        Assert.True(sound.Placement(new Loc(3, 0)).Pan > 0);
    }

    [Fact]
    public void Out_of_sight_a_loud_sound_is_distant_and_a_quiet_one_unheard()
    {
        var (game, sound, _) = Below(816);
        var heard = new List<(string Name, float Gain)>();
        sound.Played += (name, gain, _) => heard.Add((name, gain));
        var hero = game.Hero.Position;
        var hidden = game.Level.AllLocs().FirstOrDefault(p => game.Level.IsPassable(p) && !game.Level[p].Has(SquareFlags.Seen)
            && !game.Level[p].Has(SquareFlags.View) && game.Noise[p] is > 2 and <= SoundDirector.HearingRange);
        Assert.NotEqual(default, hidden);
        sound.PlayAt("MON_BITE", hidden, loud: false);
        Assert.Empty(heard);
        sound.PlayAt("BR_FIRE", hidden, loud: true);
        Assert.Single(heard);
        Assert.True(heard[0].Gain < 0.4f);
        sound.PlayAt("hit", hero.Step(Direction.East), loud: false);
        Assert.Equal(2, heard.Count);
    }

    [Fact]
    public void In_the_province_a_cache_creaks_open_and_a_slain_zone_sounds_its_end()
    {
        var game = new DungeonSession(TestData.Data, 818);
        var (sound, _) = Director();
        var heard = new List<string>();
        sound.Played += (name, _, _) => heard.Add(name);
        sound.Update(game);
        game.EnterZone("gallows_road", null);
        sound.Update(game);
        Assert.Equal("explore", sound.Mood);
        var cache = ZoneBuilder.LevelAt(game.Zone!.Caches![0].At);
        var beside = game.Level.Neighbors(cache).Where(p => game.Level.IsPassable(p) && game.FoeAt(p) is null).OrderBy(p => p.DistanceTo(cache)).First();
        game.Hero.Position = beside;
        game.Perform(new Walk(DirectionExtensions.FromOffset(cache.X - beside.X, cache.Y - beside.Y)));
        Assert.Contains("chest", heard);
        foreach (var f in game.Foes.Skip(1).ToList())
            game.Banish(f);
        var last = game.Foes.Single();
        game.Hero.Character.Heal(999);
        last.Monster.Hp = 1;
        var near = game.Level.Neighbors(last.Monster.Position).First(p => game.Level.IsPassable(p) && game.FoeAt(p) is null);
        game.Hero.Position = near;
        for (var i = 0; i < 30 && last.Monster.IsActive && !last.Monster.IsRemoved; i++)
            game.Perform(new Walk(DirectionExtensions.FromOffset(last.Monster.Position.X - game.Hero.Position.X, last.Monster.Position.Y - game.Hero.Position.Y)));
        Assert.Contains("zone-complete", heard);
    }

    [Fact]
    public void Sound_never_changes_the_game()
    {
        static (long Turn, int Hp, Loc At, string Foes) Play(bool withSound)
        {
            var game = new DungeonSession(TestData.Data, 817);
            game.Hero.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
            game.Perform(new TakeStairs(Down: true));
            var (sound, _) = Director();
            for (var i = 0; i < 300 && !game.IsOver; i++)
            {
                var d = (Direction)(i * 7 % 8);
                game.Perform(i % 3 == 0 ? new Hold() : new Walk(d));
                if (withSound)
                    sound.Update(game);
            }
            return (game.Turn, game.Hero.HitPoints, game.Hero.Position,
                string.Join(";", game.Foes.Select(f => $"{f.Monster.Race.Id}@{f.Monster.Position}:{f.Monster.Hp}")));
        }
        Assert.Equal(Play(withSound: false), Play(withSound: true));
    }

    [Theory]
    [InlineData(AudioBuffer.Automatic, false, 0)]
    [InlineData(AudioBuffer.Automatic, true, 4096)]
    [InlineData(AudioBuffer.Large, false, 2048)]
    [InlineData(AudioBuffer.ExtraLarge, false, 4096)]
    public void The_mixing_period_is_extra_large_in_a_virtual_machine(string choice, bool vm, int frames) =>
        Assert.Equal(frames, AudioBuffer.PeriodFrames(choice, vm));
}
