namespace Spellbound.Domain;

/// <summary>The kinds of land (docs/game-design.md, Tiles). All produce mana; they differ in how much it varies.</summary>
public enum TileType
{
    /// <summary>Always the same yield.</summary>
    Steady,

    /// <summary>A yield in a narrow range.</summary>
    Shifting,

    /// <summary>A yield in a wide range.</summary>
    Wild,
}

/// <summary>
/// One hex of the map. Its type is fixed when the map is made; its mana yield is decided once, when it is unveiled,
/// and kept for the whole game. Until then it is dark: nobody knows its type or yield.
/// </summary>
public sealed record Tile(Hex Hex, TileType Type, int? Yield = null)
{
    public bool IsUnveiled => Yield is not null;
}
