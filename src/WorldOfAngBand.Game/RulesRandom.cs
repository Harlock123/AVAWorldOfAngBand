using WorldOfAngBand.Engine.Randomness;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>The rules' dice rolled on the game's own generator, so one seed reproduces every roll.</summary>
public sealed class RulesRandom(GameRandom rng) : IRandomSource
{
    public int Roll(int minInclusive, int maxInclusive) => rng.RandRange(minInclusive, maxInclusive);
}
