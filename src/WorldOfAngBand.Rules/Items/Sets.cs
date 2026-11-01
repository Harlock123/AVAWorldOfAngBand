using WorldOfAngBand.Rules.Characters;

namespace WorldOfAngBand.Rules.Items;

/// <summary>
/// An item set from <c>sets.json</c> (spec, section 11): its pieces are items naming it, and wearing enough of them
/// brings each bonus whose <see cref="SetBonus.Pieces"/> is reached. Bonuses are feat effects, in force while worn.
/// </summary>
public sealed record SetDefinition(string Id, string Name, IReadOnlyList<SetBonus> Bonuses);

/// <summary>What wearing <see cref="Pieces"/> pieces of a set brings.</summary>
public sealed record SetBonus(int Pieces, string Description, IReadOnlyList<FeatEffect> Effects);
