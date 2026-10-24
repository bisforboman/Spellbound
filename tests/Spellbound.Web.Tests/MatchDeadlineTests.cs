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

    /// <summary>The person whose turn it is picks the first tile left (refused, changing nothing, for anyone else).</summary>
    private async Task<(Report Report, bool Resolved)?> PickAsync(long id, string ownerId, DateTimeOffset now)
    {
        var tile = (await store.LoadAsync(id))!.Match.ToPick[0];
        return await store.CommitAsync(id, ownerId, now, MatchJson.Serialize(new Pick(0, tile)), (loaded, seat) => loaded.Match.CheckPick(new Pick(seat, tile)));
    }

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
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        Assert.Empty(await store.HoldOverdueAsync(Start + MatchStore.StepDeadline - TimeSpan.FromMinutes(1)));
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }

    [Fact]
    public async Task At_the_deadline_a_bot_picks_for_whoever_didnt_and_the_next_pick_gets_a_new_deadline()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        var late = Start + MatchStore.StepDeadline;

        Assert.Equal([id], await store.HoldOverdueAsync(late));

        var match = (await store.LoadAsync(id))!;
        Assert.Equal((2, 1), (match.Match.OpeningUnveil, match.Match.Picker)); // bob got the last tile and picks first now
        Assert.Single(match.Match.Seats[0].Tiles);
        Assert.NotNull(match.Match.Seats[0].Trait); // alice missed the trait too, so one was drafted for her
        Assert.Equal(new Dictionary<int, int> { [0] = 1 }, match.Missed);
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
        await PickAsync(id, "alice", now);
        for (var miss = 0; miss < MatchStore.MissesBeforeBot; miss++)
        {
            // Bob's turn in the first draft, then his first pick in the second.
            now += MatchStore.StepDeadline;
            await store.HoldOverdueAsync(now);
        }

        var match = (await store.LoadAsync(id))!;
        Assert.Equal([1], match.Away);
        Assert.Equal([0, 2], match.People);
        Assert.Equal(1, match.SeatOf("bob")); // still his seat to watch
        Assert.Equal((2, 2), (match.Match.OpeningUnveil, match.Match.Picker)); // the bot picked for him: carol is next
        Assert.False((await PickAsync(id, "bob", now))!.Value.Report.Succeeded);

        // Picking resets a miss, and with alice's tile left over the third draft starts with carol.
        var resolved = await PickAsync(id, "carol", now);
        Assert.True(resolved!.Value.Resolved);
        Assert.Equal((3, 2), ((await store.LoadAsync(id))!.Match.OpeningUnveil, (await store.LoadAsync(id))!.Match.Picker));
        Assert.Empty((await store.LoadAsync(id))!.Missed.Keys.Where(s => s != 1));
    }

    [Fact]
    public async Task Waiting_for_others_to_pick_neither_counts_as_a_miss_nor_clears_one()
    {
        var id = await store.CreateAsync(["alice", "bob", "carol"], GameRules.Default, Start);
        var now = Start + MatchStore.StepDeadline;

        await store.HoldOverdueAsync(now); // alice lets her first pick go
        await PickAsync(id, "bob", now); // carol gets the last tile; the second draft starts with bob
        await PickAsync(id, "bob", now);
        await PickAsync(id, "carol", now); // alice gets the last tile, without a turn

        var match = (await store.LoadAsync(id))!;
        Assert.Equal((3, 2), (match.Match.OpeningUnveil, match.Match.Picker));
        Assert.Equal(new Dictionary<int, int> { [0] = 1 }, match.Missed); // still one miss for alice

        await PickAsync(id, "carol", now);
        await PickAsync(id, "alice", now); // her turn, taken: the miss is cleared

        Assert.Empty((await store.LoadAsync(id))!.Missed);
    }

    [Fact]
    public async Task With_one_person_left_the_deadline_goes()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        var now = Start + MatchStore.StepDeadline;
        await store.HoldOverdueAsync(now); // alice misses her pick; bob gets the last tile
        await PickAsync(id, "bob", now); // alice gets the last tile, and the third draft starts with her
        now += MatchStore.StepDeadline;
        await store.HoldOverdueAsync(now); // her second miss in a row

        var match = (await store.LoadAsync(id))!;
        Assert.Equal([0], match.Away);
        Assert.Equal(MatchPhase.Rounds, match.Match.Phase); // bob got the last tile
        Assert.Null(match.DueAt); // nobody to wait for
    }

    [Fact]
    public async Task The_host_hands_the_pickers_seat_to_a_bot_and_the_bot_picks_at_once()
    {
        var id = await InvitedAsync("bob", "carol");
        await PickAsync(id, "alice", Start);

        var report = await store.HandToBotAsync(id, "alice", 1, Start.AddHours(1));

        Assert.True(report.Succeeded);
        Assert.Equal("A bot plays seat 2 from now on.", report.Lines[0]);
        var match = (await store.LoadAsync(id))!;
        Assert.Equal([1], match.Away);
        Assert.Equal((2, 2), (match.Match.OpeningUnveil, match.Match.Picker)); // the bot picked twice; carol is next
        Assert.Empty(match.Missed); // a hand-off isn't a miss
    }

    [Fact]
    public async Task A_hand_off_while_someone_else_picks_keeps_the_deadline()
    {
        var id = await InvitedAsync("bob", "carol");
        await PickAsync(id, "alice", Start);

        Assert.True((await store.HandToBotAsync(id, "alice", 2, Start.AddHours(5))).Succeeded);

        var match = (await store.LoadAsync(id))!;
        Assert.Equal((1, 1), (match.Match.OpeningUnveil, match.Match.Picker)); // bob still picks
        Assert.Equal(Start + MatchStore.StepDeadline, match.DueAt);
        Assert.Equal([0, 1], match.People);
    }

    [Fact]
    public async Task Only_the_host_hands_a_seat_over_and_a_refusal_changes_nothing()
    {
        var id = await InvitedAsync("bob", "carol");
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var notHost = await store.HandToBotAsync(id, "bob", 2, Start);
        var self = await store.HandToBotAsync(id, "alice", 0, Start);
        var noSuchSeat = await store.HandToBotAsync(id, "alice", 7, Start);
        var botMatch = await store.HandToBotAsync(await store.CreateAsync(["alice", "bob"], GameRules.Default, Start), "alice", 1, Start);

        Assert.All(new[] { notHost, self, noSuchSeat, botMatch }, r => Assert.False(r.Succeeded));
        var match = (await store.LoadAsync(id))!;
        Assert.Empty(match.Away);
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }
}
