namespace WorldOfAngBand.Game;

/// <summary>
/// One line in the Order's book of the Lantern-sworn: who went down, how deep, and what killed them. The graveyard is
/// a list of these.
/// </summary>
public sealed record DeathRecord(
    string Name,
    string Ancestry,
    string Class,
    int Level,
    int Depth,
    int MaxDepth,
    string Killer,
    long Turns,
    ulong Seed,
    DateTimeOffset When,
    bool Won = false)
{
    /// <summary>The line as the book has it.</summary>
    public string Epitaph => Won
        ? $"{Name}, a level {Level} {Ancestry} {Class}, who put out the Cinder of Ashur and came home."
        : Depth == 0
        ? $"{Name}, a level {Level} {Ancestry} {Class}, killed by {Killer} in Gallowmere."
        : $"{Name}, a level {Level} {Ancestry} {Class}, killed by {Killer} {Depth * DungeonSession.FeetPerLevel} ft down.";
}
