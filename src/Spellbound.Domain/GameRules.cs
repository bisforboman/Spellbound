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

    // Season phases (docs/mageborn-design.md), as the share of the round gone by when each one ends, so a short round
    // has the same shape as a long one. On a 28-day round: Sanctuary days 1–3, Growth 4–10, Contest 11–17,
    // Conjunction 18–27, then the Reckoning on day 28.
    public double SanctuaryEnds { get; init; } = 3 / 28.0;
    public double GrowthEnds { get; init; } = 10 / 28.0;
    public double ContestEnds { get; init; } = 17 / 28.0;
    public double ConjunctionEnds { get; init; } = 27 / 28.0;

    // Turns
    // The turn bank (docs/mageborn-design.md): 1 turn every 10 minutes, capped at 150, a little over a day, so one
    // visit a day loses nothing.
    public TimeSpan TurnInterval { get; init; } = TimeSpan.FromMinutes(10);
    public int MaxTurns { get; init; } = 150;
    public int StartingTurns { get; init; } = 100;
    public int ProtectionTurns { get; init; } = 120;

    /// <summary>New mages are also protected for this long after creation, however many turns they spend.</summary>
    public TimeSpan ProtectionDuration { get; init; } = TimeSpan.FromHours(72);

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

    /// <summary>Exploring's yield from the Contest phase on: land now comes mainly from war.</summary>
    public double ExploreLateMultiplier { get; init; } = 0.5;

    /// <summary>Exploring's yield while your land is below the round's median, so small mages catch up.</summary>
    public double ExploreBelowMedianMultiplier { get; init; } = 1.25;
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

    /// <summary>Each battle a mage lost within this window shrinks the land the next attacker takes...</summary>
    public TimeSpan RepeatAttackWindow { get; init; } = TimeSpan.FromHours(72);

    /// <summary>...by this factor: with 0.5, a win takes 10%, then 5%, then 2.5% of the same mage's land.</summary>
    public double RepeatAttackLandFactor { get; init; } = 0.5;

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
