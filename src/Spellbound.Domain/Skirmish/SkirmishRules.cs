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

    /// <summary>Mages can attack each other's regions.</summary>
    public bool Attacks { get; init; } = true;

    /// <summary>Mages can cast hexes at each other.</summary>
    public bool Hexes { get; init; } = true;

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
    public int AttackCost { get; init; } = 2;
    public int CastCost { get; init; } = 1;
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

    // Battles (the Realm's formula: army × school counter × luck against defense × luck)
    /// <summary>A region's own guards: defense it has even with no army behind it.</summary>
    public int RegionGuard { get; init; } = 20;

    /// <summary>Defense each building adds to its region: built-up regions are harder to take.</summary>
    public int DefensePerBuilding { get; init; } = 15;

    /// <summary>
    /// A mage can only attack mages holding at least this share of its own regions (the Realm's range rule), so a
    /// leader can't pick off the smallest. 0 turns it off.
    /// </summary>
    public int AttackRangePercent { get; init; } = 60;

    /// <summary>A mage's home region can't be attacked, so nobody is wiped off the board.</summary>
    public bool HomeIsSafe { get; init; } = false;

    /// <summary>Share of a taken region's buildings destroyed in the fighting.</summary>
    public int CaptureDestroysPercent { get; init; } = 0;

    public double CombatRollSpread { get; init; } = 0.1;
    public double CounterBonus { get; init; } = 0.15;
    public int AttackerLossPercentOnWin { get; init; } = 8;
    public int AttackerLossPercentOnLoss { get; init; } = 12;
    public int DefenderLossPercentOnWin { get; init; } = 5;
    public int DefenderLossPercentOnLoss { get; init; } = 10;

    // Spells and hexes (the Realm's spell book, priced and timed for Skirmish)
    public int SelfSpellMana { get; init; } = 400;
    public int WardMana { get; init; } = 300;
    public int HexMana { get; init; } = 500;

    /// <summary>How many rounds a self-spell lasts, counting the round it's cast in.</summary>
    public int SpellRounds { get; init; } = 3;

    public double HexBaseChance { get; init; } = 0.7;
    public double HexCounterBonus { get; init; } = 0.15;
    public int HexKillPercent { get; init; } = 10;
    public int HexGoldPercent { get; init; } = 15;
    public int HexManaPercent { get; init; } = 15;
    public int HexBuildingsDestroyed { get; init; } = 1;
    public int HexActionsTaken { get; init; } = 1;

    // Players who don't ready up
    /// <summary>Rounds in a row without readying up before a mage is away: its regions defend at <see cref="AwayDefense"/>.</summary>
    public int AwayAfterMissed { get; init; } = 2;

    public double AwayDefense { get; init; } = 0.5;

    /// <summary>Rounds in a row without readying up before a mage is out: its regions turn free and it scores nothing.</summary>
    public int OutAfterMissed { get; init; } = 6;

    // Scoring
    public int PointsPerRegion { get; init; } = 3;

    /// <summary>Extra points for each Terrain 3 or Terrain 4 region.</summary>
    public int PointsPerRichRegion { get; init; } = 2;

    public int PointsPerBuilding { get; init; } = 2;

    /// <summary>Points for holding at least one region of every terrain.</summary>
    public int PointsForTerrainSet { get; init; } = 5;

    public int PointsPerBattleWon { get; init; } = 2;

    /// <summary>Public objectives drawn per game, each worth <see cref="PointsPerObjective"/> to whoever reaches it first.</summary>
    public int Objectives { get; init; } = 3;

    public int PointsPerObjective { get; init; } = 5;
    public int ObjectiveRegions { get; init; } = 6;
    public int ObjectiveBuildings { get; init; } = 12;
    public int ObjectiveBattles { get; init; } = 3;
    public int ObjectiveNodes { get; init; } = 5;
    public int ObjectiveUnits { get; init; } = 40;

    /// <summary>The game ends early when one mage holds at least this share of the board after a round.</summary>
    public double BoardShareToWin { get; init; } = 0.75;
}
