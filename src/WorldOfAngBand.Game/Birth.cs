using WorldOfAngBand.Rules.Characters;

namespace WorldOfAngBand.Game;

/// <summary>
/// What the player chooses at birth (the spec's section 5.1): the hero (null for the default Lantern-sworn, a human
/// fighter), and the rumour of how Ashen Reliquary ended (null to hear one at random; a Legacy names it: destroy, bind,
/// claim or mercy), and the birth options: <see cref="Randarts"/>, a new set of random artifacts in place of the
/// setting's own.
/// </summary>
public sealed record Birth(CharacterChoices? Character = null, string? Rumour = null, bool Randarts = false);
