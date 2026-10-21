namespace Spellbound.Domain;

/// <summary>
/// Everything about a match that changes as it is played, as plain data to store (the rules are stored beside it).
/// <see cref="Match.Save"/> writes it and <see cref="Match.Load"/> reads it back. A change to what a match holds bumps
/// <see cref="CurrentVersion"/> and teaches <see cref="Match.Load"/> to read the older shape.
/// </summary>
public sealed record MatchState(
    int Version,
    MatchPhase Phase,
    int OpeningUnveil,
    int CurrentRound,
    IReadOnlyList<Hex> OnAuction,
    IReadOnlyList<TileState> Tiles,
    IReadOnlyList<SeatState> Seats,
    IReadOnlyList<UnitsState> Units)
{
    public const int CurrentVersion = 1;
}

public sealed record TileState(Hex Hex, TileType Type, int? Yield, int Guard);

public sealed record SeatState(
    int Mana, int Score, int ProducedThisRound, int ProducedCarry, int ConquestsThisRound, IReadOnlyList<Hex> Tiles,
    int LandCarry = 0,
    IReadOnlyList<Trait>? Offer = null,
    Trait? Trait = null,
    int TraitPoints = 0,
    IReadOnlyList<Hex>? OpeningTiles = null,
    int MonstersCleared = 0,
    int RivalTilesTaken = 0,
    IReadOnlyList<Spell>? SpellsCast = null,
    int HexesCast = 0,
    int RoundsCast = 0);

public sealed record UnitsState(Hex Hex, int Imps, int Golems);
