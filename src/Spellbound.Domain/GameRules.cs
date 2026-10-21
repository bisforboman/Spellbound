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

    /// <summary>The smallest bid in an opening auction. A player who wants to spend nothing passes instead.</summary>
    public int MinBid { get; init; } = 1;

    /// <summary>The unauctioned tiles each opening Unveil reveals and leaves free.</summary>
    public int OpeningFreeTilesPerUnveil { get; init; } = 1;

    // Rounds (docs/game-design.md, Rounds and stances, Units, Taking unowned tiles, Scoring and winning)

    /// <summary>How many rounds a match lasts after the opening.</summary>
    public int Rounds { get; init; } = 8;

    public int ImpPower { get; init; } = 1;
    public int ImpCost { get; init; } = 1;
    public int GolemPower { get; init; } = 3;
    public int GolemCost { get; init; } = 2;

    /// <summary>The mana each Golem costs at the start of every round, after income.</summary>
    public int GolemUpkeep { get; init; } = 1;

    /// <summary>An Attune costs this plus <see cref="AttunePricePerTile"/> for each of the first
    /// <see cref="AttuneCheapTiles"/> tiles the player already holds, and <see cref="AttunePricePerTileBeyond"/> for
    /// each tile beyond them: cheap for a small realm, dear for a big one.</summary>
    public int AttuneBasePrice { get; init; } = 1;
    public int AttunePricePerTile { get; init; } = 1;
    public int AttuneCheapTiles { get; init; } = 3;
    public int AttunePricePerTileBeyond { get; init; } = 3;

    /// <summary>The rounds that start with a later Unveil, which reveals the tiles still dark, split evenly (an odd one
    /// goes to the earlier round). After the last of them the whole map is revealed.</summary>
    public IReadOnlyList<int> LaterUnveilRounds { get; init; } = [2, 4];

    /// <summary>A tile revealed in a later Unveil is guarded on this many faces of a die with
    /// <see cref="GuardDieFaces"/> faces: 2 in 6, about one in three.</summary>
    public int GuardedFaces { get; init; } = 2;
    public int GuardDieFaces { get; init; } = 6;

    /// <summary>The battle power of a tile's monster guard.</summary>
    public int MonsterPower { get; init; } = 3;

    // Spells (docs/game-design.md, Spells)

    /// <summary>Scorch's price for each power that actually burns; what doesn't burn (units that moved away, or a
    /// Golem too strong to burn whole) costs nothing.</summary>
    public int ScorchManaPerPower { get; init; } = 3;
    public int WardCost { get; init; } = 3;

    /// <summary>The defence a Ward adds to its tile in this round's battles.</summary>
    public int WardPower { get; init; } = 3;

    /// <summary>A Rally's price, and the power it adds to the seat's attack this round (paid even if the attack doesn't
    /// happen, as a Ward is).</summary>
    public int RallyCost { get; init; } = 3;
    public int RallyPower { get; init; } = 2;

    public int LeylineManaPerUnit { get; init; } = 1;
    public int LeylineMinCost { get; init; } = 2;
    public int BlightCost { get; init; } = 3;
    public int LureCost { get; init; } = 3;

    /// <summary>How much a Lure weakens a monster guard, for good; a guard never drops below <see cref="MinGuard"/>.</summary>
    public int LurePower { get; init; } = 1;
    public int MinGuard { get; init; } = 1;

    /// <summary>Points banked for each monster tile cleared or defended rival tile taken.</summary>
    public int PointsPerConquest { get; init; } = 2;

    /// <summary>Points for taking a rival's tile that had no defence at all (no units, Ward or Tower): a raid.</summary>
    public int PointsPerEmptyConquest { get; init; } = 1;

    /// <summary>One point for every this many tiles held, counted every round and added up: holding 4 tiles for 3 rounds
    /// is 12 tile-rounds, 4 points. The remainder carries over.</summary>
    public int TilesPerPoint { get; init; } = 3;

    /// <summary>One point for every this much mana a player's land produced; the remainder carries over.</summary>
    public int ManaPerPoint { get; init; } = 10;

    // Upgrades (docs/game-design.md, Upgrades)

    /// <summary>What an upgrade costs to build (the Build stance).</summary>
    public int UpgradeCost { get; init; } = 5;

    /// <summary>The most upgrades a realm can hold when it builds another; taken ones count.</summary>
    public int UpgradeCap { get; init; } = 3;

    public int ShrineYield { get; init; } = 1;
    public int TowerPower { get; init; } = 2;
    public int SanctumDiscount { get; init; } = 1;

    /// <summary>Points at the end for each upgrade a seat holds.</summary>
    public int PointsPerUpgrade { get; init; } = 1;

    // Traits (docs/game-design.md, Traits)

    /// <summary>The traits each seat is offered to draft one from; no trait is offered to two seats.</summary>
    public int TraitsOffered { get; init; } = 2;

    /// <summary>The most points a trait goal can add.</summary>
    public int TraitGoalCap { get; init; } = 3;

    /// <summary>Points at the end for each rival's trait guessed right, and the most that guessing can add.</summary>
    public int PointsPerTraitGuess { get; init; } = 1;
    public int TraitGuessCap { get; init; } = 3;

    /// <summary>Wildheart and Hearthkeeper: one point per this many tiles of their type.</summary>
    public int TilesOfTypePerTraitPoint { get; init; } = 1;

    /// <summary>Weaver: one point per this many tiles in the largest connected group.</summary>
    public int GroupTilesPerTraitPoint { get; init; } = 2;

    /// <summary>Weaver: the tiles of the largest group that score nothing; one point per
    /// <see cref="GroupTilesPerTraitPoint"/> beyond them (4 tiles = 1 point, 6 = 2, 8 = 3).</summary>
    public int WeaverFreeTiles { get; init; } = 2;

    /// <summary>Wanderer: the smallest group that counts (a lone tile doesn't).</summary>
    public int WandererGroupSize { get; init; } = 2;

    /// <summary>Spellweaver: one point per this many different spells cast...</summary>
    public int SpellsPerTraitPoint { get; init; } = 2;

    /// <summary>...and one more for casting a spell in this many rounds.</summary>
    public int SpellRoundsForTraitPoint { get; init; } = 6;


    /// <summary>Golemwright: one point per this many Golems at the end.</summary>
    public int GolemsPerTraitPoint { get; init; } = 3;

    /// <summary>Hexer: one point per this many Scorches and Blights cast.</summary>
    public int HexesPerTraitPoint { get; init; } = 1;

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

    public int CostOf(UnitKind kind) => kind == UnitKind.Golem ? GolemCost : ImpCost;

    public int PowerOf(UnitKind kind) => kind == UnitKind.Golem ? GolemPower : ImpPower;

    public int AttunePrice(int tilesHeld) => AttuneBasePrice + AttunePricePerTile * Math.Min(tilesHeld, AttuneCheapTiles)
        + AttunePricePerTileBeyond * Math.Max(0, tilesHeld - AttuneCheapTiles);

    public IReadOnlyList<int> YieldsOf(TileType type) => type switch
    {
        TileType.Steady => SteadyYields,
        TileType.Shifting => ShiftingYields,
        TileType.Wild => WildYields,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
