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
            await PassAsync(id, "alice", now);
            await PassAsync(id, "carol", now);
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
        Assert.Equal(MatchPhase.Rounds, (await store.LoadAsync(id))!.Match.Phase);
        Assert.DoesNotContain(0, (await store.LoadAsync(id))!.Missed.Keys);
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
        Assert.Equal(2, match.Match.OpeningUnveil);
        Assert.Empty(match.Missed); // a hand-off isn't a miss
    }

    [Fact]
    public async Task Only_the_host_hands_a_seat_over_and_a_refusal_changes_nothing()
    {
        var id = await InvitedAsync("bob", "carol");
        await PassAsync(id, "bob", Start);
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var notHost = await store.HandToBotAsync(id, "bob", 2, Start);
        var self = await store.HandToBotAsync(id, "alice", 0, Start);
        var noSuchSeat = await store.HandToBotAsync(id, "alice", 7, Start);
        var botMatch = await store.HandToBotAsync(await store.CreateAsync(["alice", "bob"], GameRules.Default, Start), "alice", 1, Start);

        Assert.All(new[] { notHost, self, noSuchSeat, botMatch }, r => Assert.False(r.Succeeded));
        var match = (await store.LoadAsync(id))!;
        Assert.Empty(match.Away);
        Assert.Equal([1], match.Committed);
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }
}
