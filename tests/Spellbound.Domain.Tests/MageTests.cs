namespace Spellbound.Domain.Tests;

// Numbers in these tests follow the defaults in GameRules / docs/game-design.md:
// 250 land, 20 nodes, 40 towns, 10,000 gold, 2,000 mana, 100 turns.
public class MageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default;

    private static Mage NewMage(MagicSchool school = MagicSchool.A, GameRules? rules = null) =>
        Mage.Create("user-1", "Merlin", school, T0, rules ?? Rules);

    [Fact]
    public void Create_uses_the_starting_values()
    {
        var mage = NewMage();

        Assert.Equal(250, mage.Land);
        Assert.Equal(190, mage.FreeLand);
        Assert.Equal(10_000, mage.Gold);
        Assert.Equal(2_000, mage.Mana);
        Assert.Equal(100, mage.Turns);
        Assert.True(mage.IsProtected(T0, Rules));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ab  ")]
    [InlineData("This name is far too long for a mage")]
    public void Create_rejects_bad_names(string name)
    {
        Assert.Throws<ArgumentException>(() => Mage.Create("user-1", name, MagicSchool.C, T0, Rules));
    }

    [Fact]
    public void Explore_gains_land_with_diminishing_returns_and_runs_the_economy()
    {
        var mage = NewMage();

        var report = mage.Explore(5, T0, Rules);

        Assert.True(report.Succeeded);
        Assert.Equal(5, report.TurnsSpent);
        // Acres per turn: 10, 9, 9, 8, 8 (shrinks as land grows).
        Assert.Equal(294, mage.Land);
        // Gold per turn = land + 10 × towns, after that turn's exploring: 660 + 669 + 678 + 686 + 694.
        Assert.Equal(10_000 + 3_387, mage.Gold);
        // Mana: 20 nodes × 10 per turn.
        Assert.Equal(3_000, mage.Mana);
        Assert.Equal(95, mage.Turns);
        Assert.Equal(5, mage.TurnsSpent);
    }

    [Fact]
    public void Spending_more_turns_than_you_have_fails_and_changes_nothing()
    {
        var mage = NewMage();

        var report = mage.Explore(101, T0, Rules);

        Assert.False(report.Succeeded);
        Assert.Equal(250, mage.Land);
        Assert.Equal(100, mage.Turns);
        Assert.Equal(10_000, mage.Gold);
    }

    [Fact]
    public void Turns_regenerate_before_an_action()
    {
        var mage = NewMage();

        var report = mage.Explore(101, T0.AddMinutes(6), Rules);

        Assert.True(report.Succeeded);
        Assert.Equal(0, mage.Turns);
    }

    [Fact]
    public void Build_costs_gold_and_turns()
    {
        var mage = NewMage();

        var report = mage.Build(BuildingType.Node, 15, T0, Rules);

        Assert.True(report.Succeeded);
        Assert.Equal(2, report.TurnsSpent);
        Assert.Equal(35, mage.Nodes);
        Assert.Equal(175, mage.FreeLand);
        // 10,000 − 15 × 500 + 2 turns × (250 + 400) gold.
        Assert.Equal(3_800, mage.Gold);
        // New nodes already produce: 2 turns × 35 × 10 mana.
        Assert.Equal(2_700, mage.Mana);
    }

    [Fact]
    public void Build_needs_free_land()
    {
        var mage = NewMage();

        var report = mage.Build(BuildingType.Town, 191, T0, Rules);

        Assert.False(report.Succeeded);
        Assert.Equal(40, mage.Towns);
    }

    [Fact]
    public void Build_needs_enough_gold()
    {
        var mage = NewMage();

        var report = mage.Build(BuildingType.Node, 25, T0, Rules);

        Assert.False(report.Succeeded);
        Assert.Equal(20, mage.Nodes);
        Assert.Equal(10_000, mage.Gold);
        Assert.Equal(100, mage.Turns);
    }

    [Fact]
    public void Meditate_adds_mana_on_top_of_node_income()
    {
        var mage = NewMage();

        mage.Meditate(3, T0, Rules);

        // 3 turns × (100 meditation + 200 from nodes).
        Assert.Equal(2_900, mage.Mana);
    }

    [Fact]
    public void Mana_is_capped_by_storage()
    {
        var rules = Rules with { BaseManaStorage = 0, ManaStoragePerNode = 110 }; // capacity 2,200
        var mage = NewMage(rules: rules);

        mage.Meditate(5, T0, rules);

        Assert.Equal(2_200, mage.Mana);
    }

    [Fact]
    public void Cast_spends_mana_and_turns_and_adds_the_effect()
    {
        var mage = NewMage(MagicSchool.A);

        var report = mage.Cast("self-a", T0, Rules);

        Assert.True(report.Succeeded);
        Assert.Equal(98, mage.Turns);
        // 2,000 − 1,500 + 2 turns × 200 node mana.
        Assert.Equal(900, mage.Mana);
        var effect = Assert.Single(mage.ActiveEffects);
        Assert.Equal("self-a", effect.SpellId);
        Assert.Equal(30, effect.RemainingTurns);
    }

    [Fact]
    public void Cast_is_limited_to_your_own_school()
    {
        var mage = NewMage(MagicSchool.A);

        var report = mage.Cast("self-b", T0, Rules);

        Assert.False(report.Succeeded);
        Assert.Empty(mage.ActiveEffects);
        Assert.Equal(2_000, mage.Mana);
    }

    [Fact]
    public void Cast_needs_enough_mana()
    {
        var rules = Rules with { StartingMana = 1_000 };
        var mage = NewMage(MagicSchool.A, rules);

        var report = mage.Cast("self-a", T0, rules);

        Assert.False(report.Succeeded);
        Assert.Equal(100, mage.Turns);
    }

    [Fact]
    public void Active_spell_boosts_income()
    {
        var mage = NewMage(MagicSchool.A);
        mage.Cast("self-a", T0, Rules);
        var goldBefore = mage.Gold;

        mage.Explore(1, T0, Rules);

        // (260 land + 400 from towns) × 1.25
        Assert.Equal(825, mage.Gold - goldBefore);
    }

    [Fact]
    public void Spell_fades_after_its_duration()
    {
        var mage = NewMage(MagicSchool.A);
        mage.Cast("self-a", T0, Rules);

        var report = mage.Meditate(30, T0, Rules);

        Assert.Empty(mage.ActiveEffects);
        Assert.Contains("Self-spell A has faded.", report.Lines);
    }

    [Fact]
    public void Recasting_refreshes_instead_of_stacking()
    {
        var mage = NewMage(MagicSchool.C);
        mage.Cast("self-c", T0, Rules);
        mage.Meditate(10, T0, Rules);

        mage.Cast("self-c", T0, Rules);

        var effect = Assert.Single(mage.ActiveEffects);
        Assert.Equal(30, effect.RemainingTurns);
    }

    [Theory]
    [InlineData(MagicSchool.A, MagicSchool.B, MagicSchool.E)]
    [InlineData(MagicSchool.E, MagicSchool.A, MagicSchool.D)]
    public void Schools_form_a_ring(MagicSchool school, MagicSchool strongAgainst, MagicSchool weakAgainst)
    {
        Assert.Equal(strongAgainst, school.StrongAgainst());
        Assert.Equal(weakAgainst, school.WeakAgainst());
    }

    [Theory]
    [InlineData("Ada", null)]
    [InlineData("  Merlin  ", null)]
    [InlineData("Mo", "Name must be 3–24 characters.")]
    [InlineData("A name that is far too long", "Name must be 3–24 characters.")]
    public void A_name_is_checked_the_same_way_everywhere(string name, string? problem)
    {
        Assert.Equal(problem, Mage.NameProblem(name));
    }
}
