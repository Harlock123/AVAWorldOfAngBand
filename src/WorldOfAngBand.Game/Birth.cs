using WorldOfAngBand.Rules.Characters;

namespace WorldOfAngBand.Game;

/// <summary>
/// What the player chooses at birth (the spec's section 5.1): the hero (null for the default Lantern-sworn, a human
/// fighter), and the rumour of how Ashen Reliquary ended (null to hear one at random; a Legacy names it: destroy, bind,
/// claim or mercy), the birth options (<see cref="Randarts"/>, a new set of random artifacts in place of the setting's
/// own), and the <see cref="Challenge"/> it is played as, if any (the daily dungeon or the weekly challenge).
/// </summary>
public sealed record Birth(CharacterChoices? Character = null, string? Rumour = null, bool Randarts = false, Challenge? Challenge = null)
{
    /// <summary>The weekly twist in play, if any.</summary>
    public string? Twist => Challenge?.Twist;
}
