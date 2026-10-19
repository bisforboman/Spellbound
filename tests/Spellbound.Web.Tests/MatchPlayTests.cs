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
}
