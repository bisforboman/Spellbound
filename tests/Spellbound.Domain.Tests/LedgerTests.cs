namespace Spellbound.Domain.Tests;

// A preview runs the action on a copy: it shows what would happen and never changes the mage.
public class LedgerTests
{
    // Exploring in the Growth phase with no median to be below: yields as before the round had phases.
    private static readonly ExploreConditions Calm = new(SeasonPhase.Growth, 0);

    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default;

    private static Mage NewMage(MagicSchool school = MagicSchool.Dawn, GameRules? rules = null) =>
        Mage.Create("user-1", "Merlin", school, T0, rules ?? Rules);

    [Fact]
    public void Explore_preview_gives_the_same_numbers_as_exploring_and_changes_nothing()
    {
        var mage = NewMage();

        var ledger = mage.Preview(T0, Rules, m => m.Explore(5, T0, Rules, Calm));

        // The same numbers as MageTests.Explore_gains_land_with_diminishing_returns_and_runs_the_economy.
        Assert.True(ledger.Report.Succeeded);
        Assert.Equal(250, ledger.Before.Land);
        Assert.Equal(296, ledger.After.Land);
        Assert.Equal(10_000 + 3_390, ledger.After.Gold);
        Assert.Equal(3_000, ledger.After.Mana);
        Assert.Equal(95, ledger.After.Turns);
        Assert.Equal(250, mage.Land);
        Assert.Equal(10_000, mage.Gold);
        Assert.Equal(100, mage.Turns);
        Assert.Equal(0, mage.TurnsSpent);
    }

    [Fact]
    public void Cast_preview_leaves_the_mages_spells_alone()
    {
        var mage = NewMage();
        mage.Cast("self-a", T0, Rules);

        var ledger = mage.Preview(T0, Rules, m => m.Meditate(30, T0, Rules));

        Assert.Contains("Dawn self-spell has faded.", ledger.Report.Warnings);
        var effect = Assert.Single(mage.ActiveEffects);
        Assert.Equal(30, effect.RemainingTurns);
    }

    [Fact]
    public void Recruit_preview_shows_desertion_and_leaves_the_army_alone()
    {
        var rules = Rules with { StartingGold = 6_000, GoldPerAcrePerTurn = 0, GoldPerTownPerTurn = 0 };
        var mage = NewMage(rules: rules);

        var ledger = mage.Preview(T0, rules, m => m.Recruit("unit-1", 100, T0, rules));

        Assert.Equal(81, ledger.After.Units);
        Assert.Equal(["You ran out of gold to pay your army: 19 units deserted."], ledger.Report.Warnings);
        Assert.Equal(0, mage.TotalUnits);
        Assert.Equal(6_000, mage.Gold);
    }

    [Fact]
    public void A_preview_counts_the_turns_regained_since_they_were_stored()
    {
        var mage = NewMage();
        var later = T0 + (Rules.TurnInterval * 3);

        var ledger = mage.Preview(later, Rules, m => m.Explore(1, later, Rules, Calm));

        Assert.Equal(103, ledger.Before.Turns);
        Assert.Equal(102, ledger.After.Turns);
        Assert.Equal(100, mage.Turns);
        Assert.Equal(T0, mage.TurnsAccruedAt);
    }

    [Fact]
    public void A_failing_preview_says_why_and_moves_nothing()
    {
        var mage = NewMage();

        var ledger = mage.Preview(T0, Rules, m => m.Build(BuildingType.Node, 25, T0, Rules));

        Assert.False(ledger.Report.Succeeded);
        Assert.Equal("Building 25 costs 12,500 gold, but you have 10,000.", ledger.Report.Lines[0]);
        Assert.Equal(ledger.Before, ledger.After);
    }

    [Fact]
    public void A_report_without_trouble_has_no_warnings()
    {
        Assert.Empty(NewMage().Explore(5, T0, Rules, Calm).Warnings);
    }
}
