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
    IReadOnlyList<Hex> OnAuction, // the tiles to draft, named for the auction the draft replaced (saved matches keep the name)
    IReadOnlyList<TileState> Tiles,
    IReadOnlyList<SeatState> Seats,
    IReadOnlyList<UnitsState> Units,
    IReadOnlyList<UpgradeState>? Upgrades = null,
    IReadOnlyList<Hex>? Blighted = null,
    IReadOnlyList<StancePick>? Picks = null, // the round's picks so far, and how many seats took the last one
    int Acted = 0,
    IReadOnlyList<WardState>? Wards = null, // this round's Wards and Blights, kept while the round is under way
    IReadOnlyList<Hex>? Blights = null)
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
    int RoundsCast = 0,
    int LastRoundCast = 0);

public sealed record UnitsState(Hex Hex, int Imps, int Golems);

public sealed record UpgradeState(Hex Hex, Upgrade Upgrade);

public sealed record WardState(Hex Hex, int Power);
