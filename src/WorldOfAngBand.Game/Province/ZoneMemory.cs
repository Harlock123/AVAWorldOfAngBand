namespace WorldOfAngBand.Game.Province;

/// <summary>
/// What a zone keeps for the hero (the spec's section 7.4): the packs they have killed (and how many dungeon levels
/// they had been down by then), the caches they have opened and the shrines they have used.
/// </summary>
public sealed class ZoneMemory
{
    /// <summary>A pack comes back once the hero has been down this many dungeon levels since it was killed.</summary>
    public const int LevelsToReturn = 5;

    public Dictionary<int, int> SlainAt { get; } = [];

    public HashSet<string> CachesOpened { get; } = [];

    public HashSet<string> ShrinesUsed { get; } = [];

    /// <summary>Whether a pack is still dead, having been killed when the hero had been down <paramref name="levelsDown"/> levels.</summary>
    public bool StillDead(int pack, int levelsDown) =>
        SlainAt.TryGetValue(pack, out var at) && levelsDown - at < LevelsToReturn;
}
