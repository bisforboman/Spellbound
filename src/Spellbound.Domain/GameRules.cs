namespace Spellbound.Domain;

/// <summary>
/// Every tunable number for a game server. Nothing outside this type should hardcode balance values.
/// Bound from configuration section "GameRules" in the web app; see docs/game-design.md.
/// </summary>
public sealed record GameRules
{
    public static GameRules Default { get; } = new();

    // Rounds
    public TimeSpan RoundLength { get; init; } = TimeSpan.FromDays(28);
    public int HallOfFameSize { get; init; } = 10;

    // Turns
    public TimeSpan TurnInterval { get; init; } = TimeSpan.FromMinutes(6);
    public int MaxTurns { get; init; } = 200;
    public int StartingTurns { get; init; } = 100;
    public int ProtectionTurns { get; init; } = 120;

    // Starting mage
    public int StartingLand { get; init; } = 250;
    public int StartingNodes { get; init; } = 20;
    public int StartingTowns { get; init; } = 40;
    public int StartingGold { get; init; } = 10_000;
    public int StartingMana { get; init; } = 2_000;

    // Economy, applied once per turn spent
    public int GoldPerAcrePerTurn { get; init; } = 1;
    public int GoldPerTownPerTurn { get; init; } = 10;
    public int ManaPerNodePerTurn { get; init; } = 10;
    public int BaseManaStorage { get; init; } = 5_000;
    public int ManaStoragePerNode { get; init; } = 1_000;

    // Actions
    public int ExploreBaseAcresPerTurn { get; init; } = 10;
    public int BuildingsPerTurn { get; init; } = 10;
    public int BuildingGoldCost { get; init; } = 500;
    public int MeditateManaPerTurn { get; init; } = 100;

    // Army (unit stats live in UnitBook)
    public int UnitsRecruitedPerTurn { get; init; } = 50;
    public int DesertionPercent { get; init; } = 10;
    public int BaseDefensePerAcre { get; init; } = 2;

    // Attacks
    public int AttackTurnCost { get; init; } = 4;
    public int AttackRangePercent { get; init; } = 60;
    public double CombatRollSpread { get; init; } = 0.1;
    public double CounterBonus { get; init; } = 0.15;
    public int LandTakenPercent { get; init; } = 10;
    public int AttackerLossPercentOnWin { get; init; } = 8;
    public int AttackerLossPercentOnLoss { get; init; } = 12;
    public int DefenderLossPercentOnWin { get; init; } = 5;
    public int DefenderLossPercentOnLoss { get; init; } = 10;

    // Hexes (offensive spells; their costs and strengths live in SpellBook)
    public double HexBaseSuccessChance { get; init; } = 0.7;
    public double HexCounterBonus { get; init; } = 0.15;

    // Networth
    public int NetworthPerAcre { get; init; } = 5;
    public int NetworthPerBuilding { get; init; } = 10;
    public int NetworthPerUnitPower { get; init; } = 1;
}
