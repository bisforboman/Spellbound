using Npgsql;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

public sealed class MatchDeadlineTests : TestDatabase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly NpgsqlDataSource source;
    private readonly MatchStore store;
    private readonly MatchLobbies lobbies;

    public MatchDeadlineTests()
    {
        source = NpgsqlDataSource.Create(ConnectionString);
        store = new MatchStore(new HostDatabase(source));
        lobbies = new MatchLobbies(new HostDatabase(source));
        foreach (var id in new[] { "alice", "bob", "carol" })
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

    private Task<(Report Report, bool Resolved)?> PassAsync(long id, string ownerId, DateTimeOffset now) =>
        store.CommitAsync(id, ownerId, now, MatchJson.Serialize(new Bid(0, null, 0)), (loaded, seat) => loaded.Match.CheckBid(new Bid(seat, null, 0)));

    /// <summary>A bid on the tile up now (refused, changing nothing, for someone who already has their tile).</summary>
    private async Task BidAsync(long id, string ownerId, int amount, DateTimeOffset now)
    {
        var tile = (await store.LoadAsync(id))!.Match.OnAuction[0];
        await store.CommitAsync(id, ownerId, now, MatchJson.Serialize(new Bid(0, tile, amount)), (loaded, seat) => loaded.Match.CheckBid(new Bid(seat, tile, amount)));
    }

    private Task BidOneAsync(long id, string ownerId, DateTimeOffset now) => BidAsync(id, ownerId, 1, now);

    /// <summary>A match started from an invite, so alice is its host.</summary>
    private async Task<long> InvitedAsync(params string[] joiners)
    {
        var (_, code) = await lobbies.OpenAsync("alice", joiners.Length + 1, GameRules.Default, Start);
        foreach (var joiner in joiners)
        {
            await lobbies.JoinAsync(code!, joiner, Start);
        }

        return (await lobbies.StartAsync(code!, "alice", GameRules.Default, Start)).GameId!.Value;
    }

    [Fact]
    public async Task A_match_with_two_people_has_a_deadline_and_one_with_a_person_has_none()
    {
        var friends = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        var solo = await store.CreateAsync(["alice", null], GameRules.Default, Start);

        Assert.Equal(Start + MatchStore.StepDeadline, (await store.LoadAsync(friends))!.DueAt);
        Assert.Null((await store.LoadAsync(solo))!.DueAt);
    }

    [Fact]
    public async Task Nothing_resolves_before_the_deadline()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        await PassAsync(id, "alice", Start);
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        Assert.Empty(await store.HoldOverdueAsync(Start + MatchStore.StepDeadline - TimeSpan.FromMinutes(1)));
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
        Assert.Equal([0], (await store.LoadAsync(id))!.Committed);
    }

    [Fact]
    public async Task At_the_deadline_whoever_hasnt_committed_holds_and_the_next_step_gets_a_new_deadline()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        await PassAsync(id, "alice", Start);
        var late = Start + MatchStore.StepDeadline;

        Assert.Equal([id], await store.HoldOverdueAsync(late));

        var match = (await store.LoadAsync(id))!;
        Assert.Equal(2, match.Match.OpeningUnveil);
        Assert.NotNull(match.Match.Seats[1].Trait); // bob missed the pick too, so a trait was drafted for him
        Assert.Equal(new Dictionary<int, int> { [1] = 1 }, match.Missed);
        Assert.Empty(match.Away);
        Assert.Equal(late + MatchStore.StepDeadline, match.DueAt);
        Assert.NotEmpty(match.LastReport);
        Assert.Empty(await store.HoldOverdueAsync(late)); // once
    }

    [Fact]
    public async Task Two_misses_in_a_row_and_a_bot_takes_the_seat_and_the_person_still_watches()
    {
        var id = await store.CreateAsync(["alice", "bob", "carol"], GameRules.Default, Start);
        var now = Start;
        for (var miss = 0; miss < MatchStore.MissesBeforeBot; miss++)
        {
            // alice and carol bid on each tile, so bob, who holds, is still bidding for the next one.
            await BidOneAsync(id, "alice", now);
            await BidOneAsync(id, "carol", now);
            now += MatchStore.StepDeadline;
            await store.HoldOverdueAsync(now);
        }

        var match = (await store.LoadAsync(id))!;
        Assert.Equal([1], match.Away);
        Assert.Equal([0, 2], match.People);
        Assert.Equal(1, match.SeatOf("bob")); // still his seat to watch
        Assert.False((await PassAsync(id, "bob", now))!.Value.Report.Succeeded);

        // Committing resets a miss: alice and carol are never counted, and the step no longer waits for bob.
        await PassAsync(id, "alice", now);
        var resolved = await PassAsync(id, "carol", now);
        Assert.True(resolved!.Value.Resolved);
        Assert.Equal((2, 2), ((await store.LoadAsync(id))!.Match.OpeningUnveil, (await store.LoadAsync(id))!.Match.AuctionTile));
        Assert.DoesNotContain(0, (await store.LoadAsync(id))!.Missed.Keys);
    }

    [Fact]
    public async Task Sitting_out_a_tile_neither_counts_as_a_miss_nor_clears_one()
    {
        var id = await store.CreateAsync(["alice", "bob", "carol"], GameRules.Default, Start);
        var now = Start;

        await BidOneAsync(id, "alice", now); // tile 1: alice and carol bid, bob lets the deadline pass
        await BidOneAsync(id, "carol", now);
        now += MatchStore.StepDeadline;
        await store.HoldOverdueAsync(now);
        Assert.Equal(1, (await store.LoadAsync(id))!.Missed[1]);

        await BidAsync(id, "bob", 5, now); // tile 2: bob wins, and the last tile goes to whoever is left
        await PassAsync(id, "alice", now);
        await PassAsync(id, "carol", now);
        Assert.Equal(2, (await store.LoadAsync(id))!.Match.OpeningUnveil);
        Assert.Empty((await store.LoadAsync(id))!.Missed); // bob committed, so his miss is cleared

        await BidAsync(id, "alice", 5, now); // the next auction: alice wins tile 1 and sits out tile 2
        await PassAsync(id, "bob", now);
        await PassAsync(id, "carol", now);
        now += MatchStore.StepDeadline;
        await store.HoldOverdueAsync(now); // tile 2: bob and carol both let it pass

        var missed = (await store.LoadAsync(id))!.Missed;
        Assert.Equal(1, missed[1]);
        Assert.Equal(1, missed[2]);
        Assert.DoesNotContain(0, missed.Keys); // alice sat it out: no miss for her
    }

    [Fact]
    public async Task A_miss_survives_a_tile_its_player_sits_out()
    {
        // A seed where nobody bids on the first tile and it lands, free, on bob, who let the deadline pass.
        for (var seed = 1; seed <= 200; seed++)
        {
            var id = await store.CreateAsync(["alice", "bob", "carol"], GameRules.Default, Start, seed);
            await PassAsync(id, "alice", Start);
            await PassAsync(id, "carol", Start);
            var late = Start + MatchStore.StepDeadline;
            await store.HoldOverdueAsync(late);
            if ((await store.LoadAsync(id))!.Match.Seats[1].Tiles.Count == 0)
            {
                continue;
            }

            await PassAsync(id, "alice", late); // tile 2, without bob
            await PassAsync(id, "carol", late);

            var match = (await store.LoadAsync(id))!;
            Assert.Equal(2, match.Match.OpeningUnveil);
            Assert.Equal(1, match.Missed[1]); // still one miss: sitting out neither adds nor clears one
            return;
        }

        Assert.Fail("No seed handed bob the first tile.");
    }

    [Fact]
    public async Task With_one_person_left_the_deadline_goes()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        var now = Start;
        for (var miss = 0; miss < MatchStore.MissesBeforeBot; miss++)
        {
            await PassAsync(id, "alice", now);
            now += MatchStore.StepDeadline;
            await store.HoldOverdueAsync(now);
        }

        var match = (await store.LoadAsync(id))!;
        Assert.Equal([1], match.Away);
        Assert.Null(match.DueAt);
        Assert.True((await PassAsync(id, "alice", now))!.Value.Resolved); // nobody to wait for
    }

    [Fact]
    public async Task The_host_hands_a_seat_to_a_bot_and_the_step_resolves_if_everyone_else_has_committed()
    {
        var id = await InvitedAsync("bob", "carol");
        await PassAsync(id, "alice", Start);
        await PassAsync(id, "carol", Start);

        var report = await store.HandToBotAsync(id, "alice", 1, Start.AddHours(1));

        Assert.True(report.Succeeded);
        Assert.Equal("A bot plays seat 2 from now on.", report.Lines[0]);
        var match = (await store.LoadAsync(id))!;
        Assert.Equal([1], match.Away);
        Assert.Equal(2, match.Match.AuctionTile); // the first tile went
        Assert.Empty(match.Missed); // a hand-off isn't a miss
    }

    [Fact]
    public async Task A_hand_off_while_others_still_choose_keeps_the_deadline()
    {
        var id = await InvitedAsync("bob", "carol");
        await PassAsync(id, "alice", Start);

        Assert.True((await store.HandToBotAsync(id, "alice", 1, Start.AddHours(5))).Succeeded);

        var match = (await store.LoadAsync(id))!;
        Assert.Equal(1, match.Match.OpeningUnveil); // carol still chooses
        Assert.Equal(Start + MatchStore.StepDeadline, match.DueAt);
        Assert.Equal([0, 2], match.People);
    }

    [Fact]
    public async Task Only_the_host_hands_a_seat_over_and_a_refusal_changes_nothing()
    {
        var id = await InvitedAsync("bob", "carol");
        await PassAsync(id, "bob", Start);
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var notHost = await store.HandToBotAsync(id, "bob", 2, Start);
        var committed = await store.HandToBotAsync(id, "alice", 1, Start);
        var self = await store.HandToBotAsync(id, "alice", 0, Start);
        var noSuchSeat = await store.HandToBotAsync(id, "alice", 7, Start);
        var botMatch = await store.HandToBotAsync(await store.CreateAsync(["alice", "bob"], GameRules.Default, Start), "alice", 1, Start);

        Assert.All(new[] { notHost, committed, self, noSuchSeat, botMatch }, r => Assert.False(r.Succeeded));
        var match = (await store.LoadAsync(id))!;
        Assert.Empty(match.Away);
        Assert.Equal([1], match.Committed);
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }
}
