using System.Text.Json;
using static Spellbound.Domain.Tests.Scenarios;

namespace Spellbound.Domain.Tests;

public class UpgradeTests
{
    private static Hex Home(Match match, int seat) => match.Seats[seat].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).First();

    private static Match Rich(int players = 3) => FirstWhere(m => Border(m, 0, 1) is not null, GameRules.Default with { StartingMana = 30 }, players);

    [Fact]
    public void Building_costs_its_price_and_puts_the_upgrade_on_the_tile()
    {
        var match = Rich();
        var home = Home(match, 0);
        var mana = match.Seats[0].Mana;

        var check = match.Check(Orders.Build(0, home, Upgrade.Tower));
        var report = Play(match, Orders.Build(0, home, Upgrade.Tower));

        Assert.StartsWith("Build a Tower", check.Lines[0], StringComparison.Ordinal);
        Assert.Equal(Upgrade.Tower, match.UpgradeOn(home));
        Assert.Contains(report.Lines, l => l == $"Seat 1 builds a Tower on {home}.");
        Assert.Equal(mana - 5, match.Seats[0].Mana - match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value));
    }

    [Fact]
    public void A_build_that_doesnt_check_changes_nothing()
    {
        var match = Rich();
        var home = Home(match, 0);
        var (_, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Build(0, home, Upgrade.Shrine));
        var before = Snapshot(match);

        Assert.Contains("isn't yours", match.Check(Orders.Build(0, theirs, Upgrade.Shrine)).Lines[0], StringComparison.Ordinal);
        Assert.Contains("already has a Shrine", match.Check(Orders.Build(0, home, Upgrade.Tower)).Lines[0], StringComparison.Ordinal);
        Assert.False(match.Resolve([Orders.Build(0, theirs, Upgrade.Shrine), Orders.Hold(1), Orders.Hold(2)], new SeededRandom(1)).Succeeded);
        Assert.Equal(before, Snapshot(match));

        var poor = Opened(1, GameRules.Default with { UpgradeCost = 1000 });
        Assert.Contains("costs 1000 mana", poor.Check(Orders.Build(0, Home(poor, 0), Upgrade.Tower)).Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_realm_holds_at_most_three_upgrades()
    {
        var match = FirstWhere(m => m.Seats[0].Tiles.Count >= 3, GameRules.Default with { StartingMana = 40, UpgradeCap = 2 });
        var tiles = match.Seats[0].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        Play(match, Orders.Build(0, tiles[0], Upgrade.Shrine));
        Play(match, Orders.Build(0, tiles[1], Upgrade.Shrine));

        var third = match.Check(Orders.Build(0, tiles[2], Upgrade.Shrine));

        Assert.False(third.Succeeded);
        Assert.Contains("already hold 2 upgrades", third.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_Shrine_raises_the_tiles_income()
    {
        var match = Rich();
        var home = Home(match, 0);
        Play(match, Orders.Build(0, home, Upgrade.Shrine));
        var mana = match.Seats[0].Mana;

        Play(match);

        Assert.Equal(mana + match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value) + 1, match.Seats[0].Mana);
    }

    [Fact]
    public void A_Tower_defends_an_empty_tile_and_passes_to_whoever_takes_it()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is { } b && m.Seats[1].Tiles.Count > 1, GameRules.Default with { StartingMana = 30 });
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 6), Orders.Build(1, theirs, Upgrade.Tower));

        var check = match.Check(Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(2, 0))]));
        Assert.Contains("power 2", check.Lines[0], StringComparison.Ordinal);
        Assert.Contains("a tie", check.Lines[0], StringComparison.Ordinal); // 2 Imps against the Tower's 2

        Play(match, Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(4, 0))]));

        Assert.Equal(0, match.OwnerOf(theirs));
        Assert.Equal(Upgrade.Tower, match.UpgradeOn(theirs));
        Assert.Equal(new Army(2, 0), match.UnitsOn(theirs));
    }

    [Fact]
    public void A_Sanctum_makes_spells_aimed_at_it_or_next_to_it_cheaper()
    {
        var match = Rich();
        var home = Home(match, 0);
        Play(match, Orders.Build(0, home, Upgrade.Sanctum));

        var ward = match.Check(Orders.Cast(0, Spell.Ward, home));

        Assert.StartsWith("Cast Ward for 2 mana (1 less for your Sanctum)", ward.Lines[0], StringComparison.Ordinal);
        Assert.Equal(2, match.SpellCostOf(Orders.Cast(0, Spell.Ward, home)));
    }

    [Fact]
    public void Each_upgrade_held_at_the_end_is_worth_a_point()
    {
        var match = Rich();
        Play(match, Orders.Build(0, Home(match, 0), Upgrade.Shrine));
        Report report = null!;
        while (match.Phase == MatchPhase.Rounds)
        {
            report = Play(match);
        }

        Assert.Contains(report.Lines, l => l.StartsWith("Seat 1's upgrades (1) are worth 1 point", StringComparison.Ordinal));
    }

    [Fact]
    public void Upgrades_survive_saving_and_older_saves_load_without_them()
    {
        var match = Rich();
        var home = Home(match, 0);
        Play(match, Orders.Build(0, home, Upgrade.Sanctum));
        var json = JsonSerializer.Serialize(match.Save());

        var loaded = Match.Load(JsonSerializer.Deserialize<MatchState>(json)!, match.Rules);
        var old = Match.Load(match.Save() with { Upgrades = null }, match.Rules);

        Assert.Equal(json, JsonSerializer.Serialize(loaded.Save()));
        Assert.Equal(Upgrade.Sanctum, loaded.UpgradeOn(home));
        Assert.Null(old.UpgradeOn(home));
    }
}
