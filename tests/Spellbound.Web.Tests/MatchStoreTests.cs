using Npgsql;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

public sealed class MatchStoreTests : TestDatabase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly NpgsqlDataSource source;
    private readonly MatchStore store;

    public MatchStoreTests()
    {
        source = NpgsqlDataSource.Create(ConnectionString);
        store = new MatchStore(new HostDatabase(source));
    }

    public override void Dispose()
    {
        source.Dispose();
        base.Dispose();
    }

    private Task AddAccountAsync(string id) => WithDbAsync(db => db.ExecuteAsync(
        "INSERT INTO accounts (id, provider, subject, email, created_at, last_sign_in_at) VALUES ($1, 'dev', $1, $1 || '@example.com', $2, $2)",
        id, Now));

    private static Report BotsPlay(LoadedMatch loaded, IRandomSource random)
    {
        var match = loaded.Match;
        return match.Phase == MatchPhase.Opening
            ? match.Auction([.. match.Seats.Select(s => Bot.ChooseBid(match, s.Index, random))], random)
            : match.Resolve([.. match.Seats.Select(s => Bot.ChooseOrders(match, s.Index, random))], random);
    }

    [Fact]
    public async Task A_new_match_is_stored_with_its_rules_state_and_players()
    {
        await AddAccountAsync("alice");
        var rules = GameRules.Default with { StartingMana = 12 };

        var id = await store.CreateAsync(["alice", null, null], rules, Now, seed: 5);
        var loaded = await store.LoadAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(["alice", null, null], loaded.Owners);
        Assert.Equal(12, loaded.Match.Rules.StartingMana);
        Assert.Equal(MatchJson.Serialize(rules), MatchJson.Serialize(loaded.Match.Rules));
        var fresh = Match.Start(3, rules, new SeededRandom(5));
        Assert.Equal(MatchJson.Serialize(fresh.Save()), MatchJson.Serialize(loaded.Match.Save()));
        Assert.Equal(MatchStore.Mode, (await WithDbAsync(db => db.LoadGameAsync(id)))!.Mode);
    }

    [Fact]
    public async Task A_match_played_through_the_store_ends_exactly_like_one_played_in_memory()
    {
        var id = await store.CreateAsync([null, null, null], GameRules.Default, Now, seed: 9);
        var memory = Match.Start(3, GameRules.Default, new SeededRandom(9));
        var steps = 0;

        while ((await store.LoadAsync(id))!.Match.Phase != MatchPhase.Ended)
        {
            var report = await store.ApplyAsync(id, Now, BotsPlay);
            var expected = BotsPlay(new LoadedMatch(id, [null, null, null], memory), MatchStore.StepRandom(9, memory));
            Assert.Equal(expected.Lines, report!.Lines);
            steps++;
        }

        Assert.Equal(3 + 8, steps);
        Assert.Equal(Now, (await WithDbAsync(db => db.LoadGameAsync(id)))!.EndedAt); // its game ended with it
        Assert.Equal(MatchJson.Serialize(memory.Save()), MatchJson.Serialize((await store.LoadAsync(id))!.Match.Save()));
    }

    [Fact]
    public async Task A_failed_action_saves_nothing()
    {
        var id = await store.CreateAsync([null, null], GameRules.Default, Now, seed: 2);
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var report = await store.ApplyAsync(id, Now, (loaded, random) => loaded.Match.Auction([Bid.Pass(0)], random));

        Assert.False(report!.Succeeded);
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }

    [Fact]
    public async Task Two_actions_on_one_match_at_once_take_turns()
    {
        var id = await store.CreateAsync([null, null, null], GameRules.Default, Now, seed: 4);

        // Each passes the auction it finds; if both read the same state, the second save would undo the first.
        var reports = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => store.ApplyAsync(id, Now,
            (loaded, random) => loaded.Match.Auction([.. loaded.Match.Seats.Select(s => Bid.Pass(s.Index))], random)))));

        Assert.All(reports, r => Assert.True(r!.Succeeded));
        Assert.Equal(3, (await store.LoadAsync(id))!.Match.OpeningUnveil);
    }

    [Fact]
    public async Task A_match_whose_game_was_ended_takes_no_more_actions()
    {
        var id = await store.CreateAsync([null, null], GameRules.Default, Now, seed: 3);
        await WithDbAsync(db => db.EndGameAsync(id, Now).ContinueWith(_ => 0));
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var report = await store.ApplyAsync(id, Now, BotsPlay);

        Assert.Equal("This match has ended.", Assert.Single(report!.Lines));
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
        Assert.Equal("Ended early by an admin", MatchStore.Describe((await store.LoadAsync(id))!.Match, isRunning: false));
    }

    [Fact]
    public async Task A_player_sees_the_matches_they_sit_in()
    {
        await AddAccountAsync("alice");
        var first = await store.CreateAsync(["alice", null], GameRules.Default, Now, seed: 1);
        await store.CreateAsync([null, null], GameRules.Default, Now, seed: 2);
        var second = await store.CreateAsync([null, "alice"], GameRules.Default, Now, seed: 3);
        await store.ApplyAsync(second, Now, BotsPlay);

        var list = await store.ListForAsync("alice");

        Assert.Equal(
            [new PlayerMatch(second, 1, "Opening auction 2 of 3", true), new PlayerMatch(first, 0, "Opening auction 1 of 3", true)],
            list);
        Assert.Empty(await store.ListForAsync("bob"));
    }

    [Fact]
    public async Task There_is_no_match_for_another_games_id()
    {
        var other = await WithDbAsync(db => db.InsertGameAsync(new NewGame("placeholder", TimeSpan.FromDays(1), "{}"), Now));

        Assert.Null(await store.LoadAsync(other.Id));
        Assert.Null(await store.ApplyAsync(other.Id, Now, BotsPlay));
        Assert.Null(await store.LoadAsync(12345));
    }

    [Fact]
    public async Task A_deleted_account_leaves_its_seat_to_a_bot_and_the_export_lists_a_players_seats()
    {
        await AddAccountAsync("alice");
        await AddAccountAsync("bob");
        var id = await store.CreateAsync([null, "alice", "bob"], GameRules.Default, Now, seed: 1);
        var game = new SpellboundGame();

        var export = MatchJson.Serialize(await WithDbAsync(db => game.ExportAsync(db, "alice")));
        await WithDbAsync(db => db.ExecuteAsync("DELETE FROM accounts WHERE id = 'bob'"));

        Assert.Equal($$"""{"matches":[{"match":{{id}},"seat":2,"committed":null}],"invites":[]}""", export);
        Assert.Equal([null, "alice", null], (await store.LoadAsync(id))!.Owners);
    }
}
