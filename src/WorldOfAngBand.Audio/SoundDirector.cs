using System.Diagnostics;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Combat;

namespace WorldOfAngBand.Audio;

/// <summary>
/// Decides what plays (the spec's section 8.1). The view calls <see cref="Update"/> every frame: the music for the
/// screen, a fight, a great foe or the place, a track at a time from its playlist; the ambience loop under it; and
/// what changed since the last frame that has a sound (the hero hurt, a condition, an ability come ready). The game's
/// events give the rest (<see cref="SoundCues"/>), each placed on screen and quieter with distance, and heard only if
/// the hero sees it or its noise carries. Sound picks with its own random generator, never the game's, and only reads
/// the session, so a seed and its commands make the same game with sound on or off.
/// </summary>
public sealed class SoundDirector : IDisposable
{
    /// <summary>A hunting monster this near and in view starts the fight's music.</summary>
    public const int CombatRange = 16;

    /// <summary>The fight's music holds this many game turns (ten of the hero's, at normal speed) after the last hunter.</summary>
    public const long CombatHoldTurns = 100;

    /// <summary>How far, as the hero's noise flows, a sound out of sight can be heard.</summary>
    public const int HearingRange = 20;

    /// <summary>The same sound within this many seconds plays once (a pack's blows in one turn).</summary>
    public const double RepeatGuardSeconds = 0.06;

    /// <summary>An ability with at least this cooldown ticks as it comes ready (shorter ones would chatter).</summary>
    public const double ReadyTickCooldown = 5;

    private readonly IAudioEngine _engine;
    private readonly Random _random = new();
    private readonly object _musicLock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _lastPlayed = [];
    private SoundSettings _settings = new();
    private string? _mood;
    private string? _track;
    private string? _ambience;
    private DungeonSession? _session;
    private long _combatUntil = long.MinValue;
    private int _hp = -1;
    private HashSet<ConditionKind> _conditions = [];
    private HashSet<string> _timed = [];
    private readonly HashSet<string> _cooling = [];

    public SoundDirector(IAudioEngine engine, SoundPack pack)
    {
        _engine = engine;
        Pack = pack;
        _engine.MusicEnded += OnMusicEnded;
    }

    public IAudioEngine Engine => _engine;

    public SoundPack Pack { get; }

    public SoundSettings Settings => _settings;

    /// <summary>The mood now chosen (title, town, explore, dungeon, deep, combat, boss, epilogue), for tests.</summary>
    public string? Mood => _mood;

    /// <summary>The ambience loop now chosen, by name, if any.</summary>
    public string? AmbienceName => _ambience;

    /// <summary>True while the isometric view is up (it turns the map forty-five degrees, which changes left and right).</summary>
    public bool Isometric { get; set; } = true;

    /// <summary>Effect names played, with their gain and pan (for tests).</summary>
    public event Action<string, float, float>? Played;

    public void Apply(SoundSettings settings)
    {
        _settings = settings;
        var master = settings.Muted ? 0 : settings.Master;
        _engine.SetVolumes(master, settings.EffectsOn ? settings.Effects : 0, settings.MusicOn ? settings.Music : 0);
        _engine.SetAmbienceVolume(settings.AmbienceOn ? settings.Ambience : 0);
        // Switched off, music and ambience stop; switched on, the next update starts them again.
        if (!settings.MusicOn || settings.Muted)
            StopMusic();
        if (!settings.AmbienceOn || settings.Muted)
            StopAmbience();
    }

    /// <summary>Follows the game: its events, and the music and ambience for the screen up or where the hero is.</summary>
    public void Update(DungeonSession session, SoundScreen screen = SoundScreen.Game)
    {
        Follow(session);
        if (screen == SoundScreen.Game && !session.IsOver)
            Watch(session);
        var playing = !_settings.Muted;
        if (playing && _settings.MusicOn)
            SetMood(screen == SoundScreen.Game ? FightMood(session) ?? Soundscape.PlaceMood(session) : Soundscape.Mood(session, screen));
        if (playing && _settings.AmbienceOn)
            SetAmbience(Soundscape.Ambience(session, screen));
    }

    /// <summary>Listens to a new game's events (and no longer to the last one's).</summary>
    private void Follow(DungeonSession session)
    {
        if (ReferenceEquals(session, _session))
            return;
        if (_session is not null)
            _session.Published -= OnEvent;
        _session = session;
        session.Published += OnEvent;
        _combatUntil = long.MinValue;
        _hp = -1;
        _conditions = [];
        _timed = [];
        _cooling.Clear();
    }

    // --- Music ---

    /// <summary>A great foe in view has the boss's music; a monster hunting the hero nearby, the fight's (held a while).</summary>
    private string? FightMood(DungeonSession session)
    {
        var hero = session.Hero.Position;
        string? mood = null;
        foreach (var foe in session.Foes)
        {
            var m = foe.Monster;
            if (!m.IsActive || m.IsRemoved || !session.Seen(foe) || m.IsAsleep)
                continue;
            if (m.Race.IsUnique || m.Race.Has(MonsterFlags.Questor))
                return Pack.Music.ContainsKey("boss") ? "boss" : "combat";
            if (m.Position.ChebyshevTo(hero) <= CombatRange && !m.Race.Has("NEVER_MOVE") || m.Position.ChebyshevTo(hero) <= 1)
                mood = "combat";
        }
        if (mood is not null)
            _combatUntil = session.Turn + CombatHoldTurns;
        return session.Turn < _combatUntil && Pack.Music.ContainsKey("combat") ? "combat" : null;
    }

    private void SetMood(string mood)
    {
        lock (_musicLock)
        {
            if (mood == _mood && _engine.CurrentMusic is not null)
                return;
            _mood = mood;
            if (Playlist(mood) is { Count: > 0 } tracks)
                PlayFrom(tracks);
            else
                StopMusic();
        }
    }

    private IReadOnlyList<string>? Playlist(string mood)
    {
        foreach (var name in Soundscape.Fallbacks(mood))
            if (Pack.Music.TryGetValue(name, out var tracks) && tracks.Count > 0)
                return tracks;
        return null;
    }

    /// <summary>A track from the playlist, not the one just played if there is another.</summary>
    private void PlayFrom(IReadOnlyList<string> tracks)
    {
        var choices = tracks.Count > 1 ? tracks.Where(t => t != _track).ToList() : tracks.ToList();
        _track = choices[_random.Next(choices.Count)];
        _engine.PlayMusic(_track, loop: false);
    }

    // On the audio thread: the next track of the same mood.
    private void OnMusicEnded()
    {
        lock (_musicLock)
        {
            if (_mood is { } mood && !_settings.Muted && _settings.MusicOn && Playlist(mood) is { Count: > 0 } tracks)
                PlayFrom(tracks);
        }
    }

    private void StopMusic()
    {
        lock (_musicLock)
        {
            _mood = null;
            _engine.StopMusic();
        }
    }

    private void SetAmbience(IReadOnlyList<string> names)
    {
        var name = names.FirstOrDefault(n => Pack.Ambience.TryGetValue(n, out var files) && files.Count > 0);
        if (name == _ambience && (name is null || _engine.CurrentAmbience is not null))
            return;
        _ambience = name;
        if (name is null)
            _engine.StopAmbience();
        else
            _engine.PlayAmbience(Pack.Ambience[name][0]);
    }

    private void StopAmbience()
    {
        _ambience = null;
        _engine.StopAmbience();
    }

    // --- Effects ---

    /// <summary>What changed since the last frame: the hero hurt, conditions and blessings, abilities come ready.</summary>
    private void Watch(DungeonSession session)
    {
        var character = session.Hero.Character;
        var hp = session.Hero.HitPoints;
        var max = Math.Max(1, session.Hero.MaxHitPoints);
        if (_hp >= 0 && hp < _hp)
        {
            Play("hero-hurt", 0.55f + 0.45f * Math.Min(1f, (_hp - hp) / (max * 0.25f)));
            if (hp * 4 < max && _hp * 4 >= max)
                Play("HITPOINT_WARN");
        }
        _hp = hp;

        var conditions = character.Conditions.Active.Select(c => c.Kind).ToHashSet();
        foreach (var kind in conditions.Except(_conditions))
            if (SoundCues.ConditionStarts(kind) is { } name)
                Play(name, 0.8f);
        if (_conditions.Except(conditions).Any(k => SoundCues.ConditionStarts(k) is not null))
            Play("RECOVER", 0.6f);
        _conditions = conditions;
        var timed = session.Hero.Timed.Keys.ToHashSet();
        foreach (var effect in timed.Except(_timed))
            if (SoundCues.TimedStarts(effect) is { } name)
                Play(name, 0.8f);
        _timed = timed;

        foreach (var ability in session.Powers.Bar)
        {
            if (ability.Cooldown < ReadyTickCooldown)
                continue;
            if (session.Powers.CooldownLeft(ability) > 0)
                _cooling.Add(ability.Id);
            else if (_cooling.Remove(ability.Id))
                Play("ready", 0.45f);
        }
    }

    private void OnEvent(IGameEvent e)
    {
        if (_session is not { } session)
            return;
        switch (e)
        {
            case StrikeEvent s:
                if (SoundCues.Strike(s) is { } strike)
                {
                    // A blow Dubtrain has no sound for (a gaze, a wail) sounds as a hit.
                    if (strike.StartsWith("MON_", StringComparison.Ordinal) && !Pack.Sounds.ContainsKey(strike))
                        strike = "MON_HIT";
                    PlayAt(strike, s.ByHero ? s.To : s.From, loud: true, gain: s.ByHero ? 1f : 0.85f);
                }
                if (!s.ByHero && s.Blocked > 0)
                    Play("block", 0.8f);
                break;
            case SpellEvent { SpellId: { } id } spell:
                if (SoundCues.Spell(id) is { } cast)
                    PlayAt(cast, spell.From, loud: true);
                break;
            case AbilityUsedEvent a:
                if (SoundCues.Ability(a) is { } ability)
                    Play(ability);
                break;
            case CueEvent c:
                if (SoundCues.Cue(c) is { } cue)
                {
                    if (c.At is { } at && at != session.Hero.Position)
                        PlayAt(cue, at, loud: c.Cue is Game.Cue.DoorBroken or Game.Cue.DoorOpened);
                    else
                        Play(cue);
                }
                break;
            case MonsterAppearedEvent { RaceId: { } raceId } appeared:
                if (Voice(session, raceId) is { } voice && session.Foes.FirstOrDefault(f => f.Monster.Id == appeared.Id) is { } foe)
                    PlayAt(voice, foe.Monster.Position, loud: false, gain: 0.7f);
                break;
            case MonsterDiedEvent died:
                Died(session, died);
                break;
            case HeroMovedEvent moved when moved.From.ChebyshevTo(moved.To) == 1 && Outdoors(session):
                Play("footstep", 0.3f);
                break;
            case ShopEnteredEvent shop:
                Play(shop.StoreId == "home" ? "STORE_HOME" : "STORE_ENTER");
                break;
            case ReadingEvent:
                Play("inventory");
                break;
            case LevelUpEvent or MythicTierEvent:
                Play("level-up");
                break;
            case SecondBreathEvent:
                Play("INVULN");
                break;
            case HeroDiedEvent:
                Play("death");
                break;
            case HeroWonEvent:
                Play("KILL_KING");
                break;
        }
    }

    private void Died(DungeonSession session, MonsterDiedEvent died)
    {
        if (session.Data.Monster(died.RaceId) is not { } race)
            return;
        var voice = Voice(session, died.RaceId);
        if (voice is not null)
            PlayAt(voice + "-death", died.At, loud: false);
        var kill = race.Has(MonsterFlags.Questor) ? "KILL_KING" : race.IsUnique ? "KILL_UNIQUE" : voice is null ? "KILL" : null;
        if (kill is not null)
            PlayAt(kill, died.At, loud: false);
    }

    private static string? Voice(DungeonSession session, string raceId)
    {
        if (session.Data.Monster(raceId) is not { } race)
            return null;
        var ashen = raceId.StartsWith(Game.Province.AshenCreature.RacePrefix, StringComparison.Ordinal)
            ? session.Province.Creature(raceId[Game.Province.AshenCreature.RacePrefix.Length..])?.Voice
            : null;
        return SoundCues.Voice(race, ashen);
    }

    /// <summary>Footsteps sound outdoors and in towns, where the hero's pace is slow enough not to chatter.</summary>
    private static bool Outdoors(DungeonSession session) =>
        session.Zone is { } zone ? !Soundscape.Indoors(zone) : session.Depth == 0;

    /// <summary>
    /// A sound at a square: placed left or right by where the square is on screen and quieter with distance (as
    /// AshenReliquary does it), and only if the hero sees the square, or, for a <paramref name="loud"/> sound, if its
    /// noise carries to the hero round the walls (then distant).
    /// </summary>
    public void PlayAt(string name, Loc at, bool loud, float gain = 1f)
    {
        if (_session is not { } session || !session.Level.InBounds(at))
            return;
        var hero = session.Hero.Position;
        var square = session.Level[at];
        var seen = at.ChebyshevTo(hero) <= 1 || square.Has(SquareFlags.Seen) || square.Has(SquareFlags.View) && square.Has(SquareFlags.Glow);
        if (!seen)
        {
            var steps = session.Noise[at];
            if (!loud || steps == WorldOfAngBand.Engine.Monsters.NoiseMap.Unreached || steps > HearingRange)
                return;
            gain *= 0.35f;
        }
        var (g, pan) = Placement(at - hero);
        Play(name, gain * g, pan);
    }

    /// <summary>The gain and pan for a sound this far from the hero (dx, dy in squares).</summary>
    public (float Gain, float Pan) Placement(Loc offset)
    {
        var distance = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y);
        var gain = (float)Math.Clamp(1.15 - distance / 18, 0.12, 1);
        // The isometric view's screen x grows with x - y; the classic views' with x.
        var pan = (float)Math.Clamp(Isometric ? (offset.X - offset.Y) / 9.0 : offset.X / 12.0, -1, 1);
        return (gain, pan);
    }

    /// <summary>
    /// Plays an effect by name, one of its files at random: <paramref name="gain"/> scales it (and the pack's volume for
    /// it), <paramref name="pan"/> places it from -1 (left) to 1 (right). Unknown names are silent.
    /// </summary>
    public void Play(string name, float gain = 1f, float pan = 0f)
    {
        if (_settings.Muted || !_settings.EffectsOn || !Pack.Sounds.TryGetValue(name, out var files) || files.Count == 0)
            return;
        var now = _clock.Elapsed.TotalSeconds;
        if (_lastPlayed.TryGetValue(name, out var last) && now - last < RepeatGuardSeconds)
            return;
        _lastPlayed[name] = now;
        _engine.PlayEffect(files[_random.Next(files.Count)], gain * Pack.Volume(name), pan);
        Played?.Invoke(name, gain, pan);
    }

    public void Dispose()
    {
        if (_session is not null)
            _session.Published -= OnEvent;
        _engine.MusicEnded -= OnMusicEnded;
        _engine.Dispose();
    }
}
