namespace Spellbound.Domain;

/// <summary>
/// A place on the hex board, in axial coordinates (docs/game-design.md, Map). The centre is (0, 0); a board of radius
/// 2 has 19 hexes, radius 3 has 37.
/// </summary>
public readonly record struct Hex(int Q, int R)
{
    private static readonly Hex[] Directions = [new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1)];

    public static Hex Centre { get; } = new(0, 0);

    /// <summary>The third axial coordinate (Q + R + S = 0).</summary>
    public int S => -Q - R;

    /// <summary>The six hexes around this one, whether or not they are on the board.</summary>
    public IEnumerable<Hex> Neighbours()
    {
        var (q, r) = (Q, R);
        return Directions.Select(d => new Hex(q + d.Q, r + d.R));
    }

    public int DistanceTo(Hex other) => (Math.Abs(Q - other.Q) + Math.Abs(R - other.R) + Math.Abs(S - other.S)) / 2;

    public bool IsNextTo(Hex other) => DistanceTo(other) == 1;

    /// <summary>Every hex within <paramref name="radius"/> of the centre, in a fixed order (by Q, then R), so a seed
    /// always deals the same board.</summary>
    public static IReadOnlyList<Hex> Within(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        var hexes = new List<Hex>();
        for (var q = -radius; q <= radius; q++)
        {
            for (var r = Math.Max(-radius, -q - radius); r <= Math.Min(radius, -q + radius); r++)
            {
                hexes.Add(new Hex(q, r));
            }
        }

        return hexes;
    }

    public override string ToString() => $"({Q}, {R})";
}
