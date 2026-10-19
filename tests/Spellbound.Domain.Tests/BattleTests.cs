namespace Spellbound.Domain.Tests;

public class BattleTests
{
    private static readonly GameRules Rules = GameRules.Default;

    [Fact]
    public void The_winner_loses_the_losers_total_and_keeps_the_difference()
    {
        Assert.Equal(new Army(3, 0), Battle.TakeLosses(new Army(8, 0), 5, Rules));
    }

    [Fact]
    public void Weak_units_die_first_and_shield_strong_ones()
    {
        // A Golem beating two Imps loses nothing: removing it would take the loss past 2.
        Assert.Equal(new Army(0, 1), Battle.TakeLosses(new Army(0, 1), 2, Rules));
        // 2 Imps and 2 Golems (power 8) losing 5: both Imps, then one Golem.
        Assert.Equal(new Army(0, 1), Battle.TakeLosses(new Army(2, 2), 5, Rules));
        // Losing 4 of the same: both Imps, then a Golem would go past the loss, so it stops.
        Assert.Equal(new Army(0, 2), Battle.TakeLosses(new Army(2, 2), 4, Rules));
    }

    [Fact]
    public void A_loss_of_the_whole_power_wipes_an_army_out()
    {
        Assert.Equal(Army.None, Battle.TakeLosses(new Army(2, 1), 5, Rules));
        Assert.Equal(Army.None, Battle.TakeLosses(new Army(2, 1), 50, Rules));
    }

    [Fact]
    public void Joined_armies_lose_their_weakest_units_first_with_seat_order_between_equals()
    {
        var left = Battle.TakeLosses([(0, new Army(2, 0)), (1, new Army(0, 1))], 3, Rules);
        Assert.Equal([(0, Army.None), (1, new Army(0, 1))], left);

        left = Battle.TakeLosses([(1, new Army(1, 0)), (0, new Army(1, 0))], 1, Rules);
        Assert.Equal([(1, new Army(1, 0)), (0, Army.None)], left);
    }
}
