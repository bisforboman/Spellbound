namespace Spellbound.Domain.Tests;

// Most tests switch protection off and start rich, so mages can field an army at once.
public class HostileTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, ProtectionDuration = TimeSpan.Zero, StartingGold = 200_000 };

    /// <summary>Always rolls the same number: 0.5 means no luck either way.</summary>
    private sealed class FixedRandom(double value) : IRandomSource
    {
        public double NextDouble() => value;
    }

    private static readonly IRandomSource Even = new FixedRandom(0.5);

    private static Mage NewMage(string name, MagicSchool school, GameRules? rules = null) =>
        Mage.Create($"user-{name}", name, school, T0, rules ?? Rules);

    private static Mage Attacker(MagicSchool school = MagicSchool.Dawn, int knights = 400)
    {
        var mage = NewMage("Merlin", school);
        mage.Recruit("unit-3", knights, T0, Rules);
        return mage;
    }

    [Fact]
    public void Winning_attack_takes_land_and_destroys_buildings_on_it()
    {
        var attacker = Attacker(MagicSchool.Dawn);
        var defender = NewMage("Morgana", MagicSchool.Ember);
        defender.Build(BuildingType.Town, 190, T0, Rules); // fully built, so lost acres take buildings with them
        var turns = attacker.Turns;

        var result = attacker.Attack(defender, T0, Rules, Even);

        Assert.True(result.Report.Succeeded);
        Assert.True(result.Report.Won);
        Assert.Equal(4, result.Report.TurnsSpent);
        Assert.Equal(turns - 4, attacker.Turns);
        Assert.Equal(275, attacker.Land);
        Assert.Equal(225, defender.Land);
        Assert.Equal(225, defender.BuiltLand);
        Assert.Equal(400 - 32, attacker.UnitCount("unit-3")); // 8% lost on a win
        Assert.Equal("You attacked Morgana and broke through, taking 25 acres.", result.Report.Lines[0]);
        Assert.Equal("Merlin (Dawn) attacked you and broke through. You lost 25 acres.", result.TargetLines[0]);
        Assert.Contains("Of your buildings, 2 nodes and 23 towns were destroyed in the fighting.", result.TargetLines);
    }

    [Fact]
    public void Losing_attack_costs_units_and_turns_but_no_land()
    {
        var attacker = Attacker(knights: 50); // attack 300 against 500 from the defender's land
        var defender = NewMage("Morgana", MagicSchool.Ember);
        defender.Recruit("unit-1", 100, T0, Rules);

        var result = attacker.Attack(defender, T0, Rules, Even);

        Assert.True(result.Report.Succeeded);
        Assert.False(result.Report.Won);
        Assert.Equal(250, attacker.Land);
        Assert.Equal(250, defender.Land);
        Assert.Equal(50 - 6, attacker.UnitCount("unit-3")); // 12% lost on a loss
        Assert.Equal(100 - 5, defender.UnitCount("unit-1")); // 5% lost when holding
        Assert.Equal("Merlin (Dawn) attacked you, but your defenses held.", result.TargetLines[0]);
    }

    [Fact]
    public void Counter_school_strengthens_the_attack()
    {
        var attacker = Attacker(MagicSchool.Dawn);
        var defender = NewMage("Morgana", MagicSchool.Grove); // Dawn is strong against Grove

        var result = attacker.Attack(defender, T0, Rules, Even);

        Assert.Contains("Your attack power was 2,760 against their defense of 500.", result.Report.Lines); // 2,400 × 1.15
        Assert.Contains("Your Dawn magic had the edge over their Grove magic.", result.Report.Lines);
    }

    [Fact]
    public void Same_seed_replays_the_same_battle()
    {
        static IReadOnlyList<string> Fight(int seed)
        {
            var attacker = Attacker(knights: 90); // 540 attack against 500 defense: the rolls decide it
            var defender = NewMage("Morgana", MagicSchool.Ember);
            return attacker.Attack(defender, T0, Rules, new SeededRandom(seed)).Report.Lines;
        }

        Assert.Equal(Fight(42), Fight(42));
    }

    [Fact]
    public void Protected_target_cannot_be_attacked_and_nothing_changes()
    {
        var rules = Rules with { ProtectionTurns = 120 };
        var attacker = NewMage("Merlin", MagicSchool.Dawn, rules);
        attacker.Recruit("unit-3", 50, T0, rules);
        attacker.Meditate(119, T0.AddHours(12), rules); // past protection
        var defender = NewMage("Morgana", MagicSchool.Ember, rules);
        var turns = attacker.Turns;

        var result = attacker.Attack(defender, T0.AddHours(12), rules, Even);

        Assert.False(result.Report.Succeeded);
        Assert.Equal("Morgana is still under protection.", result.Report.Lines[0]);
        Assert.Empty(result.TargetLines);
        Assert.Equal(turns, attacker.Turns);
        Assert.Equal(50, attacker.UnitCount("unit-3"));
        Assert.Equal(250, defender.Land);
    }

    [Fact]
    public void Protected_attacker_cannot_attack()
    {
        var rules = Rules with { ProtectionTurns = 120 };
        var attacker = NewMage("Merlin", MagicSchool.Dawn, rules);
        attacker.Recruit("unit-3", 50, T0, rules);

        var result = attacker.Attack(NewMage("Morgana", MagicSchool.Ember), T0, rules, Even);

        Assert.False(result.Report.Succeeded);
        Assert.Equal(
            "You are protected until you've spent 119 more turns, and can't attack or hex anyone until then.",
            result.Report.Lines[0]);
    }

    [Fact]
    public void New_mages_are_protected_for_a_while_however_many_turns_they_spend()
    {
        var rules = Rules with { ProtectionDuration = TimeSpan.FromHours(72) }; // and no protection turns
        var attacker = Attacker(); // created at T0, so its own protection ends at T0 + 72 h
        var born = T0.AddHours(72);
        var defender = Mage.Create("owner-Morgana", "Morgana", MagicSchool.Ember, born, rules);

        var early = attacker.Attack(defender, born.AddHours(71), rules, Even);
        var late = attacker.Attack(defender, born.AddHours(73), rules, Even);

        Assert.Equal("Morgana is still under protection.", early.Report.Lines[0]);
        Assert.True(late.Report.Succeeded);
        Assert.True(defender.IsProtected(born.AddHours(71), rules));
        Assert.False(defender.IsProtected(born.AddHours(72), rules));
    }

    [Fact]
    public void Protection_message_names_what_is_left_and_never_a_negative_count()
    {
        var rules = Rules with { ProtectionTurns = 120, ProtectionDuration = TimeSpan.FromHours(72) };
        var mage = NewMage("Merlin", MagicSchool.Dawn, rules);

        Assert.Equal("for another 3 days 0 hours and until you've spent 120 more turns", mage.DescribeProtection(T0, rules));
        mage.Meditate(100, T0, rules);
        mage.Meditate(30, T0.AddHours(5), rules); // 130 turns spent: only the time is left
        Assert.Equal("for another 2 days 19 hours", mage.DescribeProtection(T0.AddHours(5), rules));
        Assert.Equal("for another 30 minutes", mage.DescribeProtection(T0.AddHours(71.5), rules));
        Assert.Equal(string.Empty, mage.DescribeProtection(T0.AddHours(72), rules));
    }

    [Fact]
    public void Each_battle_lost_in_the_window_halves_the_land_the_next_win_takes()
    {
        var attacker = Attacker();
        var defender = NewMage("Morgana", MagicSchool.Ember);

        var first = attacker.Attack(defender, T0, Rules, Even);
        var second = attacker.Attack(defender, T0, Rules, Even);
        var third = attacker.Attack(defender, T0, Rules, Even);

        Assert.Equal("You attacked Morgana and broke through, taking 25 acres.", first.Report.Lines[0]); // 10% of 250
        Assert.Equal("You attacked Morgana and broke through, taking 11 acres.", second.Report.Lines[0]); // 5% of 225
        Assert.Equal("You attacked Morgana and broke through, taking 5 acres.", third.Report.Lines[0]); // 2.5% of 214
        Assert.Contains(
            "They had already been beaten 2 times in the last 3 days, so you took only 2.5% of their land.", third.Report.Lines);
        Assert.Contains(
            "You had already been beaten 2 times in the last 3 days, so they took only 2.5% of your land.", third.TargetLines);
        Assert.Equal(1.25, defender.LandTakenPercent(T0, Rules), 3);
    }

    [Fact]
    public void A_loss_older_than_the_window_is_forgotten()
    {
        var attacker = Attacker();
        var defender = NewMage("Morgana", MagicSchool.Ember);

        attacker.Attack(defender, T0, Rules, Even);
        var later = attacker.Attack(defender, T0.AddHours(73), Rules, Even); // the window is 72 hours

        Assert.Equal("You attacked Morgana and broke through, taking 22 acres.", later.Report.Lines[0]); // 10% of 225
        Assert.Single(defender.RecentLosses); // the old one was dropped when the new one was recorded
    }

    [Theory]
    [InlineData(24, "the last day")]
    [InlineData(72, "the last 3 days")]
    [InlineData(12, "the last 12 hours")]
    public void The_window_is_described_in_words(int hours, string expected) =>
        Assert.Equal(expected, Mage.RepeatAttackWindowText(Rules with { RepeatAttackWindow = TimeSpan.FromHours(hours) }));

    [Fact]
    public void A_battle_the_defender_held_does_not_count()
    {
        var weak = Attacker(knights: 50); // 300 attack against 500 + 300 defense
        var defender = NewMage("Morgana", MagicSchool.Ember);
        defender.Recruit("unit-1", 100, T0, Rules);

        Assert.Equal("You attacked Morgana, but their defenses held.", weak.Attack(defender, T0, Rules, Even).Report.Lines[0]);

        Assert.Empty(defender.RecentLosses);
        Assert.Equal(10, defender.LandTakenPercent(T0, Rules));
    }

    [Fact]
    public void Much_smaller_mages_are_out_of_range()
    {
        var attacker = Attacker();
        attacker.Explore(40, T0, Rules); // well past 417 acres, so 250 is below 60%
        var defender = NewMage("Morgana", MagicSchool.Ember);

        var result = attacker.Attack(defender, T0, Rules, Even);

        Assert.False(attacker.InRange(defender, Rules));
        Assert.False(result.Report.Succeeded);
        Assert.Equal(250, defender.Land);
    }

    [Fact]
    public void Attack_needs_an_army_and_a_different_target()
    {
        var mage = NewMage("Merlin", MagicSchool.Dawn);
        Assert.False(mage.Attack(NewMage("Morgana", MagicSchool.Ember), T0, Rules, Even).Report.Succeeded);

        mage.Recruit("unit-3", 10, T0, Rules);
        Assert.Equal("You can't target yourself.", mage.Attack(mage, T0, Rules, Even).Report.Lines[0]);
    }

    [Fact]
    public void Mana_drain_takes_mana_from_the_target()
    {
        var caster = NewMage("Merlin", MagicSchool.Ember);
        var target = NewMage("Morgana", MagicSchool.Dawn);

        var result = caster.CastHex("hex-c", target, T0, Rules, new FixedRandom(0));

        Assert.True(result.Report.Succeeded);
        Assert.Null(result.Report.Won); // a hex isn't a battle
        Assert.Equal(1_800, target.Mana);
        // 2,000 - 2,000 for the spell + 200 drained + 2 turns × 200 node income.
        Assert.Equal(600, caster.Mana);
        Assert.Equal("Merlin (Ember) cast Ember hex at you: 200 of your mana was drained.", result.TargetLines[0]);
    }

    [Fact]
    public void Barrier_halves_the_chance_a_hex_lands()
    {
        var caster = NewMage("Merlin", MagicSchool.Ember);
        var target = NewMage("Morgana", MagicSchool.Dawn);
        Assert.Equal(0.7, caster.HexChance(target, Rules), 3);

        Assert.True(caster.CastHex("hex-c", target, T0, Rules, Even).Report.Succeeded);
        Assert.Equal(1_800, target.Mana);

        target.Cast("ward", T0, Rules);
        var mana = target.Mana;
        caster.Meditate(20, T0, Rules);
        var result = caster.CastHex("hex-c", target, T0, Rules, Even); // 0.5 roll against a 35% chance

        Assert.True(result.Report.Succeeded);
        Assert.Equal(mana, target.Mana);
        Assert.Equal("Merlin (Ember) cast Ember hex at you, but it was turned aside.", result.TargetLines[0]);
    }

    [Fact]
    public void Counter_school_makes_hexes_likelier()
    {
        var caster = NewMage("Merlin", MagicSchool.Ember);

        Assert.Equal(0.85, caster.HexChance(NewMage("Morgana", MagicSchool.Umbra), Rules), 3);
        Assert.Equal(0.55, caster.HexChance(NewMage("Morgana", MagicSchool.Grove), Rules), 3);
    }

    [Fact]
    public void Hex_D_burns_buildings_and_hex_E_takes_turns()
    {
        var target = NewMage("Morgana", MagicSchool.Dawn);

        NewMage("Merlin", MagicSchool.Umbra).CastHex("hex-d", target, T0, Rules, new FixedRandom(0));
        NewMage("Mordred", MagicSchool.Veil, Rules with { StartingMana = 5_000 }).CastHex("hex-e", target, T0, Rules, new FixedRandom(0));

        Assert.Equal(60 - 1, target.BuiltLand); // 3% of 60
        Assert.Equal(100 - 3, target.Turns);
    }

    [Fact]
    public void Hex_without_enough_mana_or_from_another_school_changes_nothing()
    {
        var caster = NewMage("Merlin", MagicSchool.Veil); // Veil hex costs 2,500, the mage has 2,000
        var target = NewMage("Morgana", MagicSchool.Dawn);

        Assert.False(caster.CastHex("hex-e", target, T0, Rules, Even).Report.Succeeded);
        Assert.False(caster.CastHex("hex-a", target, T0, Rules, Even).Report.Succeeded);
        Assert.Equal(2_000, caster.Mana);
        Assert.Equal(100, caster.Turns);
        Assert.Equal(100, target.Turns);
    }

    [Fact]
    public void Barrier_is_known_by_every_school()
    {
        foreach (var school in Enum.GetValues<MagicSchool>())
        {
            Assert.Contains(SpellBook.ForSchool(school), s => s.Id == "ward");
            Assert.Single(SpellBook.HexesForSchool(school));
        }
    }
}
