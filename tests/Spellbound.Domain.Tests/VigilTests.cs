namespace Spellbound.Domain.Tests;

// The Vigil freezes a realm: no turns come in, the mage can't act and can't be struck (docs/game-design.md).
public class VigilTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, ProtectionDuration = TimeSpan.Zero };
    private static readonly ExploreConditions Calm = new(SeasonPhase.Growth, 0);
    private static readonly TimeSpan Interval = Rules.TurnInterval;

    private static Mage NewMage(string name = "Merlin") => Mage.Create($"owner-{name}", name, MagicSchool.Dawn, T0, Rules);

    [Fact]
    public void No_turns_come_in_during_a_vigil_and_they_resume_after_it()
    {
        var mage = NewMage();
        mage.Explore(50, T0, Rules, Calm); // 50 turns left, so the cap is far

        var report = mage.StartVigil(TimeSpan.FromHours(10), T0, Rules);

        Assert.True(report.Succeeded);
        Assert.True(mage.IsInVigil(T0.AddHours(9)));
        mage.AccrueTurns(T0.AddHours(10) + (Interval * 2), Rules);
        Assert.Equal(52, mage.Turns); // two intervals after the end, not 10 hours' worth
    }

    [Fact]
    public void Waking_early_resumes_turns_from_then_and_keeps_partial_progress()
    {
        var mage = NewMage();
        mage.Explore(50, T0, Rules, Calm);
        var start = T0 + (Interval * 0.3); // 30% of the way to the next turn

        mage.StartVigil(TimeSpan.FromHours(72), start, Rules);
        var wake = start.AddHours(5);
        Assert.True(mage.EndVigil(wake, Rules).Succeeded);

        Assert.False(mage.IsInVigil(wake));
        mage.AccrueTurns(wake + (Interval * 0.69), Rules);
        Assert.Equal(50, mage.Turns);
        mage.AccrueTurns(wake + (Interval * 0.71), Rules);
        Assert.Equal(51, mage.Turns);
    }

    [Fact]
    public void A_mage_in_a_vigil_cannot_act_and_nothing_changes()
    {
        var mage = NewMage();
        mage.StartVigil(TimeSpan.FromHours(1), T0, Rules);
        var land = mage.Land;
        var gold = mage.Gold;

        var reports = new[]
        {
            mage.Explore(5, T0, Rules, Calm),
            mage.Build(BuildingType.Town, 1, T0, Rules),
            mage.Meditate(1, T0, Rules),
            mage.Cast("self-a", T0, Rules),
            mage.Recruit("unit-1", 1, T0, Rules),
            mage.Attack(NewMage("Morgana"), T0, Rules, new FixedRandom(0)).Report,
        };

        Assert.All(reports, r => Assert.StartsWith("You are keeping a Vigil until 17 Oct 13:00 UTC", r.Lines[0], StringComparison.Ordinal));
        Assert.All(reports, r => Assert.False(r.Succeeded));
        Assert.Equal(land, mage.Land);
        Assert.Equal(gold, mage.Gold);
        Assert.Equal(100, mage.Turns);
    }

    [Fact]
    public void A_mage_in_a_vigil_cannot_be_struck()
    {
        var attacker = NewMage();
        var target = NewMage("Morgana");
        target.StartVigil(TimeSpan.FromHours(1), T0, Rules);

        Assert.Equal("Morgana is keeping a Vigil and can't be struck until it ends.", attacker.TargetProblem(target, T0, Rules));
        Assert.Null(attacker.TargetProblem(target, T0.AddHours(1), Rules));
    }

    [Fact]
    public void One_vigil_a_round_even_if_ended_early()
    {
        var mage = NewMage();
        mage.StartVigil(TimeSpan.FromHours(1), T0, Rules);
        mage.EndVigil(T0.AddMinutes(1), Rules);

        var again = mage.StartVigil(TimeSpan.FromHours(1), T0.AddHours(2), Rules);

        Assert.False(again.Succeeded);
        Assert.Equal("You have already kept your Vigil this round.", again.Lines[0]);
        Assert.False(mage.IsInVigil(T0.AddHours(2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(73)]
    public void A_vigil_lasts_up_to_72_hours(int hours)
    {
        var mage = NewMage();

        var report = mage.StartVigil(TimeSpan.FromHours(hours), T0, Rules);

        Assert.False(report.Succeeded);
        Assert.Equal("A Vigil can last up to 72 hours.", report.Lines[0]);
        Assert.Equal(0, mage.VigilsKept);
        Assert.Equal(T0, mage.TurnsAccruedAt);
    }

    [Fact]
    public void Ending_a_vigil_you_are_not_keeping_fails()
    {
        Assert.False(NewMage().EndVigil(T0, Rules).Succeeded);
    }

    private sealed class FixedRandom(double value) : IRandomSource
    {
        public double NextDouble() => value;
    }
}
