namespace Spellbound.Domain;

/// <summary>Units: recruiting, upkeep, power and networth.</summary>
public sealed partial class Mage
{
    private Dictionary<string, int> _army = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Unit counts by unit id. Unit types the mage has never had are missing.</summary>
    public IReadOnlyDictionary<string, int> Army => _army;

    public int UnitCount(string unitId) => _army.GetValueOrDefault(unitId);

    public int TotalUnits => _army.Values.Sum();

    public int AttackPower => UnitBook.All.Sum(u => UnitCount(u.Id) * u.Attack);

    /// <summary>Units' defense plus the land's own guards.</summary>
    public int DefensePower(GameRules rules) =>
        UnitBook.All.Sum(u => UnitCount(u.Id) * u.Defense) + (Land * rules.BaseDefensePerAcre);

    public int UpkeepPerTurn => UnitBook.All.Sum(u => UnitCount(u.Id) * u.GoldUpkeep);

    /// <summary>Net power, as players see it (the code keeps the older name): the published formula is
    /// <see cref="NetPowerFormula"/>, so anyone can check a ranking by hand.</summary>
    public int Networth(GameRules rules) =>
        (Land * rules.NetworthPerAcre)
        + (BuiltLand * rules.NetworthPerBuilding)
        + UnitBook.All.Sum(u => UnitCount(u.Id) * (u.Attack + u.Defense) * rules.NetworthPerUnitPower);

    /// <summary>How net power is counted, with this game's weights, in words. Grows as the rework adds parts to it
    /// (Spell Level, creatures).</summary>
    public static string NetPowerFormula(GameRules rules) =>
        $"{rules.NetworthPerAcre} per acre of land, {rules.NetworthPerBuilding} per building, and " +
        $"{rules.NetworthPerUnitPower} per point of attack and defense in your army.";

    public static int RecruitTurnCost(int count, GameRules rules) =>
        (count + rules.UnitsRecruitedPerTurn - 1) / rules.UnitsRecruitedPerTurn;

    public ActionReport Recruit(string unitId, int count, DateTimeOffset now, GameRules rules)
    {
        AccrueTurns(now, rules);
        var unit = UnitBook.Find(unitId);
        if (unit is null)
        {
            return ActionReport.Fail("There is no such unit.");
        }

        if (count <= 0)
        {
            return ActionReport.Fail("Choose at least one unit.");
        }

        var goldCost = count * unit.GoldCost;
        if (goldCost > Gold)
        {
            return ActionReport.Fail(
                $"{N(count)} {unit.Name.ToLowerInvariant()} cost {N(goldCost)} gold, but you have {N(Gold)}.");
        }

        var turns = RecruitTurnCost(count, rules);
        if (CheckTurns(turns) is { } failure)
        {
            return failure;
        }

        Gold -= goldCost;
        _army[unit.Id] = UnitCount(unit.Id) + count;
        var summary = SpendTurns(turns, rules);
        return Report(turns, summary, $"You recruited {N(count)} {unit.Name.ToLowerInvariant()} for {N(goldCost)} gold.");
    }

    /// <summary>Pays one turn of upkeep. If the gold runs out, the treasury empties and some of every unit deserts.</summary>
    private void PayUpkeep(GameRules rules, TurnSummary summary)
    {
        var upkeep = UpkeepPerTurn;
        if (upkeep <= Gold)
        {
            Gold -= upkeep;
            summary.Upkeep += upkeep;
            return;
        }

        summary.Upkeep += Gold;
        Gold = 0;
        summary.Deserted += LoseUnits(rules.DesertionPercent, roundUp: true);
    }

    /// <summary>Removes a percentage of every unit type and returns how many units were lost.</summary>
    private int LoseUnits(double percent, bool roundUp)
    {
        var lost = 0;
        foreach (var unit in UnitBook.All)
        {
            var count = UnitCount(unit.Id);
            var exact = count * percent / 100;
            var loss = Math.Min(count, (int)(roundUp ? Math.Ceiling(exact) : Math.Floor(exact)));
            if (loss > 0)
            {
                _army[unit.Id] = count - loss;
                lost += loss;
            }
        }

        return lost;
    }
}
