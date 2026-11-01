namespace WorldOfAngBand.Audio;

/// <summary>The Sound window's settings (the spec's section 8.1), kept in settings.json. Volumes run from 0 to 1.</summary>
public sealed record SoundSettings
{
    public float Master { get; init; } = 0.8f;
    public float Music { get; init; } = 0.55f;
    public float Effects { get; init; } = 0.9f;
    public float Ambience { get; init; } = 0.45f;
    public bool MusicOn { get; init; } = true;
    public bool EffectsOn { get; init; } = true;
    public bool AmbienceOn { get; init; } = true;

    /// <summary>Everything silent (Ctrl+M), the volumes kept.</summary>
    public bool Muted { get; init; }

    /// <summary>OpenAL's mixing period (<see cref="AudioBuffer"/>): Automatic is extra large in a virtual machine.</summary>
    public string Buffer { get; init; } = AudioBuffer.Automatic;
}
