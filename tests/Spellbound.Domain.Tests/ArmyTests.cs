namespace Spellbound.Domain.Tests;

public class ArmyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default;

    private static Mage NewMage(GameRules? rules = null) =>
        Mage.Create("user-1", "Merlin", MagicSchool.A, T0, rules ?? Rules);

    [Fact]
    public void Recruit_costs_gold_and_turns_and_adds_units()
    {
        var mage = NewMage();

        var report = mage.Recruit("unit-2", 50, T0, Rules);

        Assert.True(report.Succeeded);
        Assert.Equal(1, report.TurnsSpent);
        Assert.Equal(50, mage.UnitCount("unit-2"));
        // 10,000 - 7,500 for the soldiers + 650 income - 100 upkeep (50 × 2).
        Assert.Equal(3_050, mage.Gold);
        Assert.Equal(150, mage.AttackPower);
        Assert.Equal(150 + 500, mage.DefensePower(Rules));
        Assert.Contains("Your army's upkeep cost 100 gold.", report.Lines);
    }

    [Fact]
    public void Recruit_takes_a_turn_per_50_units()
    {
        Assert.Equal(1, Mage.RecruitTurnCost(50, Rules));
        Assert.Equal(2, Mage.RecruitTurnCost(51, Rules));
    }

    [Fact]
    public void Recruit_without_enough_gold_changes_nothing()
    {
        var mage = NewMage();

        var report = mage.Recruit("unit-3", 41, T0, Rules);

        Assert.False(report.Succeeded);
        Assert.Equal(0, mage.TotalUnits);
        Assert.Equal(10_000, mage.Gold);
        Assert.Equal(100, mage.Turns);
    }

    [Fact]
    public void Recruit_rejects_unknown_units()
    {
        var mage = NewMage();

        Assert.False(mage.Recruit("dragons", 1, T0, Rules).Succeeded);
        Assert.Equal(100, mage.Turns);
    }

    [Fact]
    public void Upkeep_is_paid_every_turn_spent()
    {
        var mage = NewMage();
        mage.Recruit("unit-2", 50, T0, Rules);
        var gold = mage.Gold;

        mage.Meditate(3, T0, Rules);

        Assert.Equal(gold + (3 * (650 - 100)), mage.Gold);
    }

    [Fact]
    public void Unpaid_units_desert()
    {
        var rules = Rules with { StartingGold = 6_000, GoldPerAcrePerTurn = 0, GoldPerTownPerTurn = 0 };
        var mage = NewMage(rules);

        var report = mage.Recruit("unit-1", 100, T0, rules);

        // Two turns with no gold: 10% leave each turn (rounded up), 100 → 90 → 81.
        Assert.True(report.Succeeded);
        Assert.Equal(81, mage.UnitCount("unit-1"));
        Assert.Equal(0, mage.Gold);
        Assert.Contains("You ran out of gold to pay your army: 19 units deserted.", report.Lines);
    }

    [Fact]
    public void Networth_counts_land_buildings_and_units()
    {
        var mage = NewMage();
        Assert.Equal((250 * 5) + (60 * 10), mage.Networth(Rules));

        mage.Recruit("unit-3", 10, T0, Rules);

        Assert.Equal((250 * 5) + (60 * 10) + (10 * 8), mage.Networth(Rules));
    }
}
