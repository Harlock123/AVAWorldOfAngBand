using WorldOfAngBand.Game;

namespace WorldOfAngBand.Audio;

/// <summary>
/// Decides what plays (the spec's section 8.1): the music for the mood of the place or screen, a track at a time from
/// its playlist, and the ambience loop under it. The view calls <see cref="Update"/> every frame; it acts only when
/// the mood or the loop changes. Sound picks with its own random generator, never the game's, so a seed and its
/// commands make the same game with sound on or off.
/// </summary>
public sealed class SoundDirector : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly Random _random = new();
    private readonly object _musicLock = new();
    private SoundSettings _settings = new();
    private string? _mood;
    private string? _track;
    private string? _ambience;

    public SoundDirector(IAudioEngine engine, SoundPack pack)
    {
        _engine = engine;
        Pack = pack;
        _engine.MusicEnded += OnMusicEnded;
    }

    public IAudioEngine Engine => _engine;

    public SoundPack Pack { get; }

    public SoundSettings Settings => _settings;

    /// <summary>The mood now chosen (title, town, explore, dungeon, deep, epilogue...), for tests.</summary>
    public string? Mood => _mood;

    /// <summary>The ambience loop now chosen, by name, if any.</summary>
    public string? AmbienceName => _ambience;

    /// <summary>Effect names played (for tests).</summary>
    public event Action<string>? Played;

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

    /// <summary>Follows the game: the music and ambience for the screen up, or for where the hero is.</summary>
    public void Update(DungeonSession session, SoundScreen screen = SoundScreen.Game)
    {
        var playing = !_settings.Muted;
        if (playing && _settings.MusicOn)
            SetMood(Soundscape.Mood(session, screen));
        if (playing && _settings.AmbienceOn)
            SetAmbience(Soundscape.Ambience(session, screen));
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

    /// <summary>
    /// Plays an effect by name, one of its files at random: <paramref name="gain"/> scales it (and the pack's volume for
    /// it), <paramref name="pan"/> places it from -1 (left) to 1 (right). Unknown names are silent.
    /// </summary>
    public void Play(string name, float gain = 1f, float pan = 0f)
    {
        if (_settings.Muted || !_settings.EffectsOn || !Pack.Sounds.TryGetValue(name, out var files) || files.Count == 0)
            return;
        _engine.PlayEffect(files[_random.Next(files.Count)], gain * Pack.Volume(name), pan);
        Played?.Invoke(name);
    }

    public void Dispose()
    {
        _engine.MusicEnded -= OnMusicEnded;
        _engine.Dispose();
    }
}
