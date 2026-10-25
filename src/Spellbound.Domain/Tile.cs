namespace Spellbound.Domain;

/// <summary>The kinds of land (docs/game-design.md, Tiles). The first three produce mana and differ in how much it
/// varies; Barren land produces none.</summary>
public enum TileType
{
    /// <summary>Always the same yield.</summary>
    Steady,

    /// <summary>A yield in a narrow range.</summary>
    Shifting,

    /// <summary>A yield in a wide range.</summary>
    Wild,

    /// <summary>No yield at all (owner's choice, 25 Oct 2026): only ever revealed in a later Unveil, never guarded. It
    /// still counts as land, for points, borders and Attune prices.</summary>
    Barren,
}

/// <summary>
/// One hex of the map. Its type is fixed when the map is made. Until it is unveiled it is dark: nobody knows its type.
/// Its mana yield is rolled from its type's range: when it is unveiled, and again for each round's income while
/// someone holds it, so <see cref="Yield"/> is its latest roll (a Steady tile always rolls the same). A tile revealed
/// in a later Unveil may be guarded by monsters: <see cref="Guard"/> is their battle power, 0 when unguarded.
/// </summary>
public sealed record Tile(Hex Hex, TileType Type, int? Yield = null, int Guard = 0)
{
    public bool IsUnveiled => Yield is not null;

    public bool IsGuarded => Guard > 0;
}
