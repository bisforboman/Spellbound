namespace Spellbound.Domain.Skirmish;

/// <summary>
/// Every Skirmish number and every switch a game's creator can set (docs/skirmish-design.md). A Skirmish game stores
/// its own copy, like a Realm round stores its <see cref="GameRules"/>. First guesses: the simulator tunes them.
/// </summary>
public sealed record SkirmishRules
{
    public static SkirmishRules Default { get; } = new();

    // A game (switches)
    public int Players { get; init; } = 4;
    public int Rounds { get; init; } = 12;
    public int ActionsPerRound { get; init; } = 3;

    /// <summary>The discovery deck is on: claiming a region draws a card.</summary>
    public bool Discoveries { get; init; } = true;

    // The board
    public int RegionsPerPlayer { get; init; } = 4;
    public int SlotsPerRegion { get; init; } = 4;

    /// <summary>Extra links between regions, as a share of the region count, on top of the ring that connects them all.</summary>
    public double ExtraLinks { get; init; } = 0.5;

    // A mage's start
    public int StartingGold { get; init; } = 1_500;
    public int StartingMana { get; init; } = 1_000;
    public int StartingNodes { get; init; } = 1;
    public int StartingTowns { get; init; } = 1;

    // Orders
    public int ClaimCost { get; init; } = 1;
    public int BuildCost { get; init; } = 1;
    public int RecruitCost { get; init; } = 1;
    public int BuildingsPerOrder { get; init; } = 2;
    public int BuildingGoldCost { get; init; } = 300;
    public int UnitsPerOrder { get; init; } = 20;

    // Income, once a round after the orders
    public int GoldPerRegion { get; init; } = 100;
    public int GoldPerTown { get; init; } = 60;
    public int ManaPerNode { get; init; } = 50;

    /// <summary>Unit upkeep per round, as a multiple of a unit's <see cref="UnitDefinition.GoldUpkeep"/>.</summary>
    public int UpkeepPerRound { get; init; } = 1;

    public int DesertionPercent { get; init; } = 10;

    // Terrain
    public int Terrain1ExtraSlots { get; init; } = 1;
    public double Terrain2TownGold { get; init; } = 1.2;
    public double Terrain3NodeMana { get; init; } = 1.5;
    public double Terrain4Defense { get; init; } = 1.5;

    // The discovery deck: how many of each card
    public int Card1Count { get; init; } = 12;
    public int Card2Count { get; init; } = 6;
    public int Card3Count { get; init; } = 5;
    public int Card4Count { get; init; } = 3;
    public int Card5Count { get; init; } = 4;
    public int Card2Gold { get; init; } = 600;
    public int Card3Mana { get; init; } = 400;
    public int Card4Points { get; init; } = 3;
    public int Card5ArmyLossPercent { get; init; } = 10;

    // Scoring
    public int PointsPerRegion { get; init; } = 3;

    /// <summary>Extra points for each Terrain 3 or Terrain 4 region.</summary>
    public int PointsPerRichRegion { get; init; } = 2;

    public int PointsPerBuilding { get; init; } = 1;

    /// <summary>Points for holding at least one region of every terrain.</summary>
    public int PointsForTerrainSet { get; init; } = 5;

    /// <summary>The game ends early when one mage holds at least this share of the board after a round.</summary>
    public double BoardShareToWin { get; init; } = 0.5;
}
