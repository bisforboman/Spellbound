using Npgsql;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

public sealed class MatchNoticeTests : TestDatabase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly NpgsqlDataSource source;
    private readonly PushSink push = new();
    private readonly MatchStore store;
    private readonly MatchLobbies lobbies;

    public MatchNoticeTests()
    {
        source = NpgsqlDataSource.Create(ConnectionString);
        store = new MatchStore(new HostDatabase(source), push);
        lobbies = new MatchLobbies(new HostDatabase(source), push);
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

    private List<PushMessage> Sent()
    {
        var sent = new List<PushMessage>();
        while (push.Outbox.TryRead(out var message))
        {
            sent.Add(message);
        }

        return sent;
    }

    /// <summary>The person whose turn it is picks the first tile left.</summary>
    private async Task PickAsync(long id, string ownerId, DateTimeOffset now)
    {
        var tile = (await store.LoadAsync(id))!.Match.ToPick[0];
        await store.CommitAsync(id, ownerId, now, MatchJson.Serialize(new Pick(0, tile)), (loaded, seat) => loaded.Match.CheckPick(new Pick(seat, tile)));
    }

    /// <summary>The person lets their turn go by: Channel on a pick, a pass on a stance going round.</summary>
    private async Task PassAsync(long id, string ownerId)
    {
        var loaded = (await store.LoadAsync(id))!;
        var orders = loaded.Match.Passing(loaded.SeatOf(ownerId)!.Value);
        await store.CommitAsync(id, ownerId, Start, MatchJson.Serialize(orders), (l, seat) => l.Match.Check(orders with { Seat = seat }));
    }

    /// <summary>Everyone picks in turn until the rounds begin.</summary>
    private async Task ThroughTheOpeningAsync(long id)
    {
        while ((await store.LoadAsync(id))!.Match is { Phase: MatchPhase.Opening } match)
        {
            await PickAsync(id, (await store.LoadAsync(id))!.Owners[match.Picker!.Value]!, Start);
        }
    }

    [Fact]
    public async Task A_turn_in_a_round_tells_only_whoever_takes_the_next()
    {
        var id = await store.CreateAsync(["alice", "bob", "carol"], GameRules.Default, Start);
        await ThroughTheOpeningAsync(id);
        Sent();

        await PassAsync(id, "alice"); // round 1 starts with alice: she channels, and bob picks next

        var notice = Assert.Single(Sent());
        Assert.Equal("bob", notice.OwnerId);
        Assert.Equal($"Match {id}: Round 1, pick 1 resolved", notice.Title);
        Assert.Equal($"match/{id}", notice.Url);
    }

    [Fact]
    public async Task A_pick_tells_only_whoever_picks_next()
    {
        var id = await store.CreateAsync(["alice", "bob", "carol", null], GameRules.Default, Start);

        await PickAsync(id, "alice", Start);

        var notice = Assert.Single(Sent()); // alice is looking at the page, and carol's turn hasn't come
        Assert.Equal("bob", notice.OwnerId);
        Assert.Equal($"Match {id}: Opening draft 1, pick 1 resolved", notice.Title);
        Assert.Equal("Opening draft 1 of 3, pick 2 of 4: your move.", notice.Body);
    }

    [Fact]
    public async Task A_deadline_tells_everyone_and_a_bot_taking_over_says_so()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        var late = Start + MatchStore.StepDeadline;

        await store.HoldOverdueAsync(late); // alice lets her pick go: a bot picks for her, and bob picks next
        Assert.Equal(["bob"], Sent().Select(m => m.OwnerId));

        await PickAsync(id, "bob", late); // the second draft: bob picks, alice gets the last tile, and the third is hers
        Sent();
        await store.HoldOverdueAsync(late + MatchStore.StepDeadline);

        var alice = Assert.Single(Sent(), m => m.OwnerId == "alice");
        Assert.Equal($"Match {id}: a bot plays your seat now", alice.Title);
    }

    [Fact]
    public async Task Joining_tells_the_host_starting_tells_the_joiners_and_a_hand_off_tells_its_player()
    {
        var (_, code) = await lobbies.OpenAsync("alice", 3, GameRules.Default, Start);
        await lobbies.JoinAsync(code!, "bob", Start);
        var joined = Assert.Single(Sent());
        Assert.Equal(("alice", "Someone joined your invite", $"join/{code}"), (joined.OwnerId, joined.Title, joined.Url));

        var (_, id) = await lobbies.StartAsync(code!, "alice", GameRules.Default, Start);
        var started = Assert.Single(Sent());
        Assert.Equal(("bob", $"match/{id}"), (started.OwnerId, started.Url));

        await store.HandToBotAsync(id!.Value, "alice", 1, Start);
        var handed = Assert.Single(Sent()); // alice hasn't committed, so nothing resolves
        Assert.Equal(("bob", $"Match {id}: a bot plays your seat now"), (handed.OwnerId, handed.Title));
    }

    [Fact]
    public async Task A_refused_change_sends_nothing()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);

        await store.CommitAsync(id, "alice", Start, "{}", (_, _) => Report.Fail("No."));
        await store.HandToBotAsync(id, "bob", 0, Start);
        await lobbies.JoinAsync("nosuchcode", "bob", Start);

        Assert.Empty(Sent());
    }

    [Fact]
    public void An_attack_on_a_rivals_land_marks_its_owner_as_attacked()
    {
        var (match, mine, theirs) = Enumerable.Range(1, 2000).Select(Bordered).First(b => b is not null)!.Value;
        var attack = Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(2, 0))]);

        var (report, attacked) = MatchSteps.Resolve(match, new Dictionary<int, string> { [0] = MatchJson.Serialize(attack) },
            new HashSet<int> { 1, 2 }, new SeededRandom(1));

        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        Assert.Equal([1], attacked);
    }

    [Fact]
    public void The_notice_for_an_attacked_seat_says_so_and_the_end_reaches_watchers_too()
    {
        var random = new SeededRandom(1);
        var match = Match.Start(3, GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Draft(new Pick(match.Picker!.Value, match.ToPick[0]), random);
        }

        var loaded = new LoadedMatch(7, ["alice", "bob", "carol"], match) { Away = new HashSet<int> { 2 } };

        var notices = MatchNotices.Resolved(loaded, "round 3", new HashSet<int> { 1 }, new HashSet<int>(), actor: 0).ToList();

        var bob = Assert.Single(notices); // alice resolved it, carol is away
        Assert.Equal(("bob", "Match 7: you were attacked"), (bob.OwnerId, bob.Title));
        Assert.StartsWith("In round 3. ", bob.Body, StringComparison.Ordinal);
    }

    /// <summary>A three-seat match in its first round where seat 0 has 2 Imps on a tile next to seat 1's and may take the
    /// Attack going round, or null.</summary>
    private static (Match Match, Hex Mine, Hex Theirs)? Bordered(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Draft(new Pick(match.Picker!.Value, match.ToPick[0]), random);
        }

        foreach (var mine in match.Seats[0].Tiles)
        {
            if (match.Seats[1].Tiles.FirstOrDefault(mine.IsNextTo) is var theirs && match.Seats[1].Tiles.Any(mine.IsNextTo))
            {
                // Seat 1 summons on its pick; then Seat 2 picks Attack without using it, and it goes round to Seat 1.
                var turns = new[] { Orders.Summon(0, mine, UnitKind.Imp, 2), Orders.Pass(1, Stance.Summon), Orders.Pass(2, Stance.Summon),
                    Orders.Pass(1, Stance.Attack), Orders.Pass(2, Stance.Attack) };
                return turns.All(t => match.Take(t, random).Succeeded) ? (match, mine, theirs) : null;
            }
        }

        return null;
    }
}
