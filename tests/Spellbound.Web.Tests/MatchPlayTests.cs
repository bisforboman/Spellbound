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

        var report = await play.BidAsync(id, "alice", tile, 5); // more than a bot bids (half its mana at most)
        var view = (await play.ViewAsync(id, "alice"))!;

        Assert.True(report.Succeeded);
        Assert.Equal([tile], view.Match.Seats[0].Tiles);
        Assert.Equal(2, view.Match.OpeningUnveil); // the bots bid the rest of the auction among themselves at once
        Assert.All(view.Match.Seats, s => Assert.Single(s.Tiles));
        Assert.Equal(report.Lines, view.LastReport);
    }

    [Fact]
    public async Task The_player_drafts_a_trait_and_the_bots_draft_theirs_with_the_first_bid()
    {
        var id = await play.StartAsync("alice", 3, GameRules.Default);
        var offer = (await play.ViewAsync(id, "alice"))!.Match.Seats[0].Offer;
        var notOffered = Enum.GetValues<Trait>().First(t => !offer.Contains(t));
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var refused = await play.TraitAsync(id, "alice", notOffered);
        Assert.False(refused.Succeeded);
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));

        var chosen = await play.TraitAsync(id, "alice", offer[1]);
        var drafted = (await play.ViewAsync(id, "alice"))!.Match;
        Assert.True(chosen.Succeeded);
        Assert.Equal(offer[1], drafted.Seats[0].Trait);
        Assert.All(drafted.Seats.Skip(1), s => Assert.Null(s.Trait));

        Assert.True((await play.BidAsync(id, "alice", null, 0)).Succeeded);
        var bid = (await play.ViewAsync(id, "alice"))!;
        Assert.All(bid.Match.Seats, s => Assert.Contains(s.Trait!.Value, s.Offer));
        Assert.DoesNotContain(bid.LastReport, l => l.Contains("trait", StringComparison.OrdinalIgnoreCase)); // theirs stay secret
    }

    [Fact]
    public async Task Passing_through_the_opening_reaches_the_rounds()
    {
        var id = await play.StartAsync("alice", 2, GameRules.Default);

        await Opening.PassAsync(play, id, "alice");

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
    public async Task With_two_people_a_step_waits_until_both_have_committed()
    {
        var id = await store.CreateAsync(["alice", "bob", null], GameRules.Default, DateTimeOffset.UtcNow);
        var before = (await play.ViewAsync(id, "alice"))!.LastReport;

        var first = await play.BidAsync(id, "alice", null, 0);
        var waiting = (await play.ViewAsync(id, "bob"))!;

        Assert.True(first.Succeeded);
        Assert.Contains("Waiting for Seat 2", first.Lines[^1], StringComparison.Ordinal);
        Assert.Equal(1, waiting.Match.OpeningUnveil); // nothing resolved
        Assert.Equal([0], waiting.Committed); // bob sees that alice committed, never what
        Assert.Equal(before, waiting.LastReport);

        var second = await play.BidAsync(id, "bob", null, 0);
        var after = (await play.ViewAsync(id, "alice"))!;

        Assert.True(second.Succeeded);
        Assert.Equal((1, 2), (after.Match.OpeningUnveil, after.Match.AuctionTile)); // the first tile is settled
        Assert.Empty(after.Committed);
        Assert.Equal(second.Lines, after.LastReport);
    }

    [Fact]
    public async Task Whoever_wins_a_tile_sits_out_the_rest_of_the_auction_and_the_step_waits_only_on_the_others()
    {
        var id = await store.CreateAsync(["alice", "bob", null, null], GameRules.Default, DateTimeOffset.UtcNow);
        var tile = (await play.ViewAsync(id, "alice"))!.Match.OnAuction[0];

        await play.BidAsync(id, "alice", tile, 5); // more than a bot bids
        await play.BidAsync(id, "bob", null, 0);
        var view = (await play.ViewAsync(id, "bob"))!;

        Assert.Equal([tile], view.Match.Seats[0].Tiles);
        Assert.Equal((1, 2), (view.Match.OpeningUnveil, view.Match.AuctionTile));
        Assert.Equal([1], view.InStep); // alice has her tile: the step waits on bob alone
        Assert.False((await play.BidAsync(id, "alice", null, 0)).Succeeded);

        var after = await play.BidAsync(id, "bob", null, 0); // bob passes again: the bots settle the tile, or one lands on bob
        Assert.True(after.Succeeded);
        Assert.NotEqual((1, 2), ((await play.ViewAsync(id, "bob"))!.Match.OpeningUnveil, (await play.ViewAsync(id, "bob"))!.Match.AuctionTile));
    }

    [Fact]
    public async Task A_commitment_can_be_taken_back_until_the_step_resolves()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, DateTimeOffset.UtcNow);
        await play.BidAsync(id, "alice", null, 0);

        Assert.True(await play.WithdrawAsync(id, "alice"));
        Assert.Empty((await play.ViewAsync(id, "bob"))!.Committed);
        Assert.False(await play.WithdrawAsync(id, "alice")); // nothing left to take back

        var tile = (await play.ViewAsync(id, "alice"))!.Match.OnAuction[0];
        await play.BidAsync(id, "alice", tile, 2); // a new bid replaces nothing that's still there
        await play.BidAsync(id, "bob", null, 0);

        Assert.Contains(tile, (await play.ViewAsync(id, "alice"))!.Match.Seats[0].Tiles);
    }

    [Fact]
    public async Task A_bad_bid_after_committing_keeps_the_first_commitment()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, DateTimeOffset.UtcNow);
        var tile = (await play.ViewAsync(id, "alice"))!.Match.OnAuction[0];
        await play.BidAsync(id, "alice", tile, 2);

        Assert.False((await play.BidAsync(id, "alice", tile, 100)).Succeeded);
        Assert.Equal([0], (await play.ViewAsync(id, "bob"))!.Committed);

        await play.BidAsync(id, "bob", null, 0);
        Assert.Contains(tile, (await play.ViewAsync(id, "alice"))!.Match.Seats[0].Tiles); // the 2-mana bid stood
    }

    [Fact]
    public async Task With_two_people_a_round_resolves_both_players_orders()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, DateTimeOffset.UtcNow);
        await Opening.PassAsync(play, id, "alice", "bob");

        var match = (await play.ViewAsync(id, "alice"))!.Match;
        var mine = match.Seats[0].Tiles.First();
        var theirs = match.Seats[1].Tiles.First();

        var first = await play.OrdersAsync(id, "alice", Orders.Summon(0, mine, UnitKind.Imp, 2));
        Assert.Equal(1, (await play.ViewAsync(id, "bob"))!.Match.CurrentRound);
        Assert.Contains("Waiting for Seat 2", first.Lines[^1], StringComparison.Ordinal);

        await play.OrdersAsync(id, "bob", Orders.Summon(1, theirs, UnitKind.Imp, 1));
        var after = (await play.ViewAsync(id, "bob"))!.Match;

        Assert.Equal(2, after.CurrentRound);
        Assert.Equal(new Army(2, 0), after.UnitsOn(mine)); // alice's orders came back from her row intact
        Assert.Equal(new Army(1, 0), after.UnitsOn(theirs));
    }

    [Fact]
    public async Task A_trait_pick_is_not_the_matchs_last_report()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, DateTimeOffset.UtcNow);
        var offer = (await play.ViewAsync(id, "alice"))!.Match.Seats[0].Offer;

        Assert.True((await play.TraitAsync(id, "alice", offer[0])).Succeeded);

        Assert.Empty((await play.ViewAsync(id, "bob"))!.LastReport); // bob never sees alice's pick
    }

    private async Task<long> InRoundsAsync(int players = 3)
    {
        var id = await play.StartAsync("alice", players, GameRules.Default);
        await Opening.PassAsync(play, id, "alice");
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
