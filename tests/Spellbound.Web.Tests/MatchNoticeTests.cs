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

    private Task<(Report Report, bool Resolved)?> PassAsync(long id, string ownerId, DateTimeOffset now) =>
        store.CommitAsync(id, ownerId, now, MatchJson.Serialize(new Bid(0, null, 0)), (loaded, seat) => loaded.Match.CheckBid(new Bid(seat, null, 0)));

    [Fact]
    public async Task The_last_one_left_hears_that_the_others_are_waiting()
    {
        var id = await store.CreateAsync(["alice", "bob", "carol"], GameRules.Default, Start);

        await PassAsync(id, "alice", Start);
        Assert.Empty(Sent()); // two still choosing

        await PassAsync(id, "bob", Start);
        var waiting = Assert.Single(Sent());
        Assert.Equal("carol", waiting.OwnerId);
        Assert.Equal($"Match {id}: they're waiting on you", waiting.Title);
        Assert.Equal($"match/{id}", waiting.Url);

        await PassAsync(id, "bob", Start); // a change of mind
        Assert.Empty(Sent());
    }

    [Fact]
    public async Task A_resolved_step_tells_everyone_but_whoever_resolved_it()
    {
        var id = await store.CreateAsync(["alice", "bob", null], GameRules.Default, Start);
        await PassAsync(id, "alice", Start);
        Sent();

        var tile = (await store.LoadAsync(id))!.Match.OnAuction[0];
        await store.CommitAsync(id, "bob", Start, MatchJson.Serialize(new Bid(1, tile, 5)), (loaded, seat) => loaded.Match.CheckBid(new Bid(seat, tile, 5)));

        var notice = Assert.Single(Sent()); // bob won the tile and is looking at the page
        Assert.Equal("alice", notice.OwnerId);
        Assert.Equal($"Match {id}: Opening auction 1, tile 1 resolved", notice.Title);
        Assert.Equal("Opening auction 1 of 3, tile 2 of 3: your move.", notice.Body);
    }

    [Fact]
    public async Task A_deadline_tells_everyone_and_a_bot_taking_over_says_so()
    {
        var id = await store.CreateAsync(["alice", "bob"], GameRules.Default, Start);
        await PassAsync(id, "alice", Start);
        Sent();

        await store.HoldOverdueAsync(Start + MatchStore.StepDeadline);
        Assert.Equal(["alice", "bob"], Sent().Select(m => m.OwnerId).Order());

        await PassAsync(id, "alice", Start + MatchStore.StepDeadline);
        Sent();
        await store.HoldOverdueAsync(Start + (2 * MatchStore.StepDeadline));

        var bob = Assert.Single(Sent(), m => m.OwnerId == "bob");
        Assert.Equal($"Match {id}: a bot plays your seat now", bob.Title);
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
        var match = Match.Start(3, GameRules.Default, new SeededRandom(1));
        var loaded = new LoadedMatch(7, ["alice", "bob", "carol"], match) { Away = new HashSet<int> { 2 } };

        var notices = MatchNotices.Resolved(loaded, "round 3", new HashSet<int> { 1 }, new HashSet<int>(), actor: 0).ToList();

        var bob = Assert.Single(notices); // alice resolved it, carol is away
        Assert.Equal(("bob", "Match 7: you were attacked"), (bob.OwnerId, bob.Title));
        Assert.StartsWith("In round 3. ", bob.Body, StringComparison.Ordinal);
    }

    /// <summary>A three-seat match in its first round where seat 0 has 2 Imps on a tile next to seat 1's, or null.</summary>
    private static (Match Match, Hex Mine, Hex Theirs)? Bordered(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Bidders.Select(Bid.Pass)], random);
        }

        foreach (var mine in match.Seats[0].Tiles)
        {
            if (match.Seats[1].Tiles.FirstOrDefault(mine.IsNextTo) is var theirs && match.Seats[1].Tiles.Any(mine.IsNextTo))
            {
                var summon = match.Resolve([Orders.Summon(0, mine, UnitKind.Imp, 2), Orders.Hold(1), Orders.Hold(2)], random);
                return summon.Succeeded ? (match, mine, theirs) : null;
            }
        }

        return null;
    }
}
