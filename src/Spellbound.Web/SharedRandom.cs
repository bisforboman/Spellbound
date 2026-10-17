using Spellbound.Domain;

namespace Spellbound.Web;

/// <summary>The app's luck for battles and hexes. Thread-safe; tests swap in a seeded source.</summary>
public sealed class SharedRandom : IRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();
}
