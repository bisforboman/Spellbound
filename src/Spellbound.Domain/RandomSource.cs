namespace Spellbound.Domain;

/// <summary>Where the game gets its luck from (the map, Unveils, leftover tiles). Inject a seeded source so a whole
/// game can be replayed.</summary>
public interface IRandomSource
{
    /// <summary>A number in [0, 1).</summary>
    double NextDouble();

    /// <summary>A whole number in [0, <paramref name="count"/>).</summary>
    int Next(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return Math.Min(count - 1, (int)(NextDouble() * count));
    }
}

/// <summary>Deterministic randomness: the same seed gives the same draws. Not thread-safe.</summary>
public sealed class SeededRandom(int seed) : IRandomSource
{
    private readonly Random random = new(seed);

    public double NextDouble() => random.NextDouble();
}
