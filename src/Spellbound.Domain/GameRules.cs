namespace Spellbound.Domain;

/// <summary>
/// Every number the game runs on (docs/game-design.md); nothing else hardcodes them. Each game stores the rules it
/// runs under. All of them are first guesses until playtested.
/// </summary>
public sealed record GameRules
{
    public static GameRules Default { get; } = new();

    // Map

    /// <summary>The board's radius for up to this many players: 2 (19 hexes) for 2–3 players.</summary>
    public int SmallMapMaxPlayers { get; init; } = 3;
    public int SmallMapRadius { get; init; } = 2;

    /// <summary>The board's radius for more players: 3 (37 hexes) for 4–5.</summary>
    public int LargeMapRadius { get; init; } = 3;

    public int MinPlayers { get; init; } = 2;
    public int MaxPlayers { get; init; } = 5;

    // Setup and the opening (docs/game-design.md, Resources and Unveil)

    /// <summary>The mana each player starts with; it also pays the opening's bids.</summary>
    public int StartingMana { get; init; } = 9;

    /// <summary>How many Unveils the opening has. Each auctions one tile per player and leaves one free.</summary>
    public int OpeningUnveils { get; init; } = 3;

    /// <summary>The unauctioned tiles each opening Unveil reveals and leaves free.</summary>
    public int OpeningFreeTilesPerUnveil { get; init; } = 1;

    // Tiles: the possible yields of each type, one entry per face of a die, so a type's spread and odds are both here.
    // On average every type yields 2.

    public IReadOnlyList<int> SteadyYields { get; init; } = [2, 2, 2, 2, 2, 2];
    public IReadOnlyList<int> ShiftingYields { get; init; } = [1, 1, 2, 2, 3, 3];
    public IReadOnlyList<int> WildYields { get; init; } = [0, 1, 2, 2, 3, 4];

    public int MapRadius(int players)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, MaxPlayers);
        return players <= SmallMapMaxPlayers ? SmallMapRadius : LargeMapRadius;
    }

    public IReadOnlyList<int> YieldsOf(TileType type) => type switch
    {
        TileType.Steady => SteadyYields,
        TileType.Shifting => ShiftingYields,
        TileType.Wild => WildYields,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
