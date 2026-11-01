namespace WorldOfAngBand.Rules.Rules;

/// <summary>All randomness goes through this so a seed reproduces every roll (and tests can script them).</summary>
public interface IRandomSource
{
    /// <summary>A whole number from <paramref name="minInclusive"/> to <paramref name="maxInclusive"/>.</summary>
    int Roll(int minInclusive, int maxInclusive);
}

/// <summary>A seeded source. <see cref="Random(int)"/> uses a fixed algorithm, so a seed gives the same rolls on every platform.</summary>
public sealed class SeededRandom(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    public int Seed { get; } = seed;

    public int Roll(int minInclusive, int maxInclusive) => _random.Next(minInclusive, maxInclusive + 1);
}

public static class RandomSourceExtensions
{
    public static int D20(this IRandomSource random) => random.Roll(1, 20);

    /// <summary>A flat check: a bare d20 with no modifiers, passing on <paramref name="dc"/> or higher.</summary>
    public static bool FlatCheck(this IRandomSource random, int dc) => random.D20() >= dc;
}
