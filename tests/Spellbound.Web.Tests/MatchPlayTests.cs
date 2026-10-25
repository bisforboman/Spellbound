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
    public async Task A_pick_is_followed_at_once_by_the_bots_picks_and_is_shown_after()
    {
        var id = await play.StartAsync("alice", 3, GameRules.Default);
        var tile = (await play.ViewAsync(id, "alice"))!.Match.ToPick[1];

        var report = await play.PickAsync(id, "alice", tile);
        var view = (await play.ViewAsync(id, "alice"))!;

        Assert.True(report.Succeeded);
        Assert.Contains(tile, view.Match.Seats[0].Tiles);
        Assert.Equal(4, view.Match.Seats[0].Mana); // picks are free
        // The bots pick until it is alice's turn again: the rest of the first draft, all of the second (whose last
        // tile is hers, without a turn), and the third's first pick.
        Assert.Equal(2, view.Match.Seats[0].Tiles.Count);
        Assert.Equal((3, 2, 0), (view.Match.OpeningUnveil, view.Match.PickNumber, view.Match.Picker));
        Assert.Equal([0], view.InStep);
        Assert.Equal(7, view.LastReport.Count(l => l.StartsWith("Seat ", StringComparison.Ordinal)));
        Assert.Equal(report.Lines, view.LastReport);
    }

    [Fact]
    public async Task The_player_drafts_a_trait_and_the_bots_draft_theirs_as_they_pick()
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

        Assert.True((await play.PickAsync(id, "alice", drafted.ToPick[0])).Succeeded);
        var picked = (await play.ViewAsync(id, "alice"))!;
        Assert.All(picked.Match.Seats, s => Assert.Contains(s.Trait!.Value, s.Offer));
        Assert.DoesNotContain(picked.LastReport, l => l.Contains("trait", StringComparison.OrdinalIgnoreCase)); // theirs stay secret
    }

    [Fact]
    public async Task Picking_through_the_opening_reaches_the_rounds()
    {
        var id = await play.StartAsync("alice", 2, GameRules.Default);

        await Opening.PickAsync(play, id, "alice");

        var match = (await play.ViewAsync(id, "alice"))!.Match;
        Assert.Equal(MatchPhase.Rounds, match.Phase);
        Assert.Equal(3, match.Seats[0].Tiles.Count); // the bot may have attuned already: with 2 players, round 1 starts with it
        Assert.Equal(0, match.Actor);
    }

    [Fact]
    public async Task Only_the_seated_player_whose_turn_it_is_can_pick_and_a_bad_pick_changes_nothing()
    {
        var id = await store.CreateAsync(["alice", "bob", null], GameRules.Default, DateTimeOffset.UtcNow);
        var match = (await play.ViewAsync(id, "alice"))!.Match;
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var stranger = await play.PickAsync(id, "carol", match.ToPick[0]);
        var notYours = await play.PickAsync(id, "bob", match.ToPick[0]);
        var dark = await play.PickAsync(id, "alice", match.Map.Dark.First().Hex);
        var missing = await play.PickAsync(12345, "alice", match.ToPick[0]);

        Assert.Equal("You don't sit in this match.", Assert.Single(stranger.Lines));
        Assert.Contains("It's Seat 1's turn", Assert.Single(notYours.Lines), StringComparison.Ordinal);
        Assert.Contains("isn't one of the tiles to pick from", Assert.Single(dark.Lines), StringComparison.Ordinal);
        Assert.Equal("There is no such match.", Assert.Single(missing.Lines));
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }

    [Fact]
    public async Task With_two_people_the_draft_waits_on_whoever_picks_next()
    {
        var id = await store.CreateAsync(["alice", "bob", null], GameRules.Default, DateTimeOffset.UtcNow);
        Assert.Equal([0], (await play.ViewAsync(id, "bob"))!.InStep);

        var first = await play.PickAsync(id, "alice", (await play.ViewAsync(id, "alice"))!.Match.ToPick[0]);
        var after = (await play.ViewAsync(id, "alice"))!;

        Assert.True(first.Succeeded);
        Assert.Equal((1, 2), (after.Match.OpeningUnveil, after.Match.PickNumber)); // bob is next, and nobody waits on alice
        Assert.Equal([1], after.InStep);
        Assert.Empty(after.Committed);
        Assert.Equal(first.Lines, after.LastReport);
    }

    [Fact]
    public async Task With_two_people_each_turn_waits_on_whoever_takes_it_and_a_pick_goes_round()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, DateTimeOffset.UtcNow);
        await Opening.PickAsync(play, id, "alice", "bob");

        var match = (await play.ViewAsync(id, "alice"))!.Match;
        var mine = match.Seats[0].Tiles.First();
        var theirs = match.Seats[1].Tiles.First();
        Assert.Equal([1], (await play.ViewAsync(id, "alice"))!.InStep); // with 2 players, round 1 starts with seat 2

        Assert.False((await play.OrdersAsync(id, "alice", Orders.Summon(0, mine, UnitKind.Imp, 2))).Succeeded);
        Assert.True((await play.OrdersAsync(id, "bob", Orders.Summon(1, theirs, UnitKind.Imp, 1))).Succeeded);
        Assert.Equal([0], (await play.ViewAsync(id, "bob"))!.InStep); // alice may summon too
        Assert.True((await play.OrdersAsync(id, "alice", Orders.Summon(0, mine, UnitKind.Imp, 2))).Succeeded);
        Assert.True((await play.OrdersAsync(id, "alice", Orders.Channel(0))).Succeeded); // her pick: nobody follows Channel
        var after = (await play.ViewAsync(id, "bob"))!.Match;

        Assert.Equal(2, after.CurrentRound);
        Assert.Equal(new Army(2, 0), after.UnitsOn(mine));
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
        await Opening.PickAsync(play, id, "alice");
        return id;
    }

    [Fact]
    public async Task Orders_resolve_the_round_with_the_bots()
    {
        var id = await InRoundsAsync();
        var tile = (await play.ViewAsync(id, "alice"))!.Match.Seats[0].Tiles.First();

        // The seat in the orders is filled in from the player's own. Then the bots take their turns until hers comes again.
        var report = await play.OrdersAsync(id, "alice", Orders.Summon(2, tile, UnitKind.Imp, 2));
        var match = (await play.ViewAsync(id, "alice"))!.Match;

        Assert.True(report.Succeeded);
        Assert.Equal(new Army(2, 0), match.UnitsOn(tile));
        Assert.Contains("Seat 1 summons 2 Imps", string.Join('\n', report.Lines), StringComparison.Ordinal);
        Assert.Contains(report.Lines, l => l.StartsWith("Seat 2 ", StringComparison.Ordinal));
        Assert.Equal(0, match.Actor);
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
        Assert.False((await play.OrdersAsync(id, "bob", Orders.Channel(0))).Succeeded);
    }

    [Fact]
    public async Task Holding_to_the_end_finishes_the_match_and_its_game()
    {
        var id = await InRoundsAsync(2);

        var view = (await play.ViewAsync(id, "alice"))!;
        while (view.Match.Phase == MatchPhase.Rounds)
        {
            Assert.True((await play.OrdersAsync(id, "alice", view.Match.Passing(0))).Succeeded);
            view = (await play.ViewAsync(id, "alice"))!;
        }

        Assert.Equal(MatchPhase.Ended, view.Match.Phase);
        Assert.False(view.IsRunning);
        Assert.NotEmpty(view.Match.Winners);
        Assert.Equal("This match has ended.", Assert.Single((await play.OrdersAsync(id, "alice", Orders.Channel(0))).Lines));
    }
}
