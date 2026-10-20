using Npgsql;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

public sealed class MatchPlayTests : TestDatabase
{
    private readonly NpgsqlDataSource source;
    private readonly MatchStore store;
    private readonly MatchPlay play;

    public MatchPlayTests()
    {
        source = NpgsqlDataSource.Create(ConnectionString);
        store = new MatchStore(new HostDatabase(source));
        play = new MatchPlay(store, TimeProvider.System);
        foreach (var id in new[] { "alice", "bob" })
        {
            WithDbAsync(db => db.ExecuteAsync(
                "INSERT INTO accounts (id, provider, subject, email, created_at, last_sign_in_at) VALUES ($1, 'dev', $1, $1 || '@example.com', now(), now())",
                id)).GetAwaiter().GetResult();
        }
    }

    public override void Dispose()
    {
        source.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task A_new_match_seats_the_player_first_and_bots_in_the_rest()
    {
        var id = await play.StartAsync("alice", 4, GameRules.Default);

        var view = await play.ViewAsync(id, "alice");

        Assert.Equal(["alice", null, null, null], view!.Owners);
        Assert.Equal(37, view.Match.Map.Count);
        Assert.Null(await play.ViewAsync(id, "bob")); // only the players in it can see it
    }

    [Fact]
    public async Task A_bid_settles_the_auction_with_the_bots_and_is_shown_after()
    {
        var id = await play.StartAsync("alice", 3, GameRules.Default);
        var tile = (await play.ViewAsync(id, "alice"))!.Match.OnAuction[0];

        var report = await play.BidAsync(id, "alice", tile, 2);
        var view = (await play.ViewAsync(id, "alice"))!;

        Assert.True(report.Succeeded);
        Assert.Equal(2, view.Match.OpeningUnveil);
        Assert.Single(view.Match.Seats[0].Tiles);
        Assert.Equal(report.Lines, view.LastReport);
    }

    [Fact]
    public async Task Passing_through_the_opening_reaches_the_rounds()
    {
        var id = await play.StartAsync("alice", 2, GameRules.Default);

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await play.BidAsync(id, "alice", null, 0)).Succeeded);
        }

        var match = (await play.ViewAsync(id, "alice"))!.Match;
        Assert.Equal(MatchPhase.Rounds, match.Phase);
        Assert.All(match.Seats, s => Assert.Equal(3, s.Tiles.Count));
    }

    [Fact]
    public async Task Only_a_seated_player_can_bid_and_a_bad_bid_changes_nothing()
    {
        var id = await play.StartAsync("alice", 3, GameRules.Default);
        var tile = (await play.ViewAsync(id, "alice"))!.Match.OnAuction[0];
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var stranger = await play.BidAsync(id, "bob", tile, 2);
        var tooMuch = await play.BidAsync(id, "alice", tile, 100);
        var missing = await play.BidAsync(12345, "alice", tile, 2);

        Assert.Equal("You don't sit in this match.", Assert.Single(stranger.Lines));
        Assert.Contains("can bid from 1 to 9", Assert.Single(tooMuch.Lines), StringComparison.Ordinal);
        Assert.Equal("There is no such match.", Assert.Single(missing.Lines));
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }

    [Fact]
    public async Task A_match_with_two_people_isnt_played_at_once()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, DateTimeOffset.UtcNow);

        var report = await play.BidAsync(id, "alice", null, 0);

        Assert.False(report.Succeeded);
        Assert.Contains("other players", report.Lines[0], StringComparison.Ordinal);
    }

    private async Task<long> InRoundsAsync(int players = 3)
    {
        var id = await play.StartAsync("alice", players, GameRules.Default);
        for (var i = 0; i < 3; i++)
        {
            await play.BidAsync(id, "alice", null, 0);
        }

        return id;
    }

    [Fact]
    public async Task Orders_resolve_the_round_with_the_bots()
    {
        var id = await InRoundsAsync();
        var tile = (await play.ViewAsync(id, "alice"))!.Match.Seats[0].Tiles.First();

        // The seat in the orders is filled in from the player's own.
        var report = await play.OrdersAsync(id, "alice", Orders.Summon(2, tile, UnitKind.Imp, 2));
        var match = (await play.ViewAsync(id, "alice"))!.Match;

        Assert.True(report.Succeeded);
        Assert.Equal(2, match.CurrentRound);
        Assert.Equal(new Army(2, 0), match.UnitsOn(tile));
        Assert.Contains("Seat 1 summons 2 Imps", string.Join('\n', report.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_orders_change_nothing()
    {
        var id = await InRoundsAsync();
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));
        var tile = (await play.ViewAsync(id, "alice"))!.Match.Seats[0].Tiles.First();

        var report = await play.OrdersAsync(id, "alice", Orders.Summon(0, tile, UnitKind.Golem, 1000));

        Assert.False(report.Succeeded);
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
        Assert.False((await play.OrdersAsync(id, "bob", Orders.Hold(0))).Succeeded);
    }

    [Fact]
    public async Task Holding_to_the_end_finishes_the_match_and_its_game()
    {
        var id = await InRoundsAsync(2);

        for (var round = 1; round <= 8; round++)
        {
            Assert.True((await play.OrdersAsync(id, "alice", Orders.Hold(0))).Succeeded);
        }

        var view = (await play.ViewAsync(id, "alice"))!;
        Assert.Equal(MatchPhase.Ended, view.Match.Phase);
        Assert.False(view.IsRunning);
        Assert.NotEmpty(view.Match.Winners);
        Assert.Equal("This match has ended.", Assert.Single((await play.OrdersAsync(id, "alice", Orders.Hold(0))).Lines));
    }
}
