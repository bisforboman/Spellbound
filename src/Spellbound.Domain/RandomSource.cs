namespace Spellbound.Domain;

/// <summary>Where battles and hexes get their luck from. Inject a seeded source to replay a fight.</summary>
public interface IRandomSource
{
    /// <summary>A number in [0, 1).</summary>
    double NextDouble();
}

/// <summary>Deterministic randomness: the same seed gives the same rolls. Not thread-safe.</summary>
public sealed class SeededRandom(int seed) : IRandomSource
{
    private readonly Random random = new(seed);

    public double NextDouble() => random.NextDouble();
}
