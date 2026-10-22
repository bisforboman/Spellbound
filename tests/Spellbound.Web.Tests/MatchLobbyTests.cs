using Npgsql;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

public sealed class MatchLobbyTests : TestDatabase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly NpgsqlDataSource source;
    private readonly MatchLobbies lobbies;
    private readonly MatchStore store;

    public MatchLobbyTests()
    {
        source = NpgsqlDataSource.Create(ConnectionString);
        lobbies = new MatchLobbies(new HostDatabase(source));
        store = new MatchStore(new HostDatabase(source));
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

    private async Task<string> OpenAsync(int seats = 3) =>
        (await lobbies.OpenAsync("alice", seats, GameRules.Default, Now)).Code!;

    [Fact]
    public async Task The_host_starts_with_the_joiners_in_order_and_bots_in_the_free_seats()
    {
        var code = await OpenAsync(4);
        Assert.True((await lobbies.JoinAsync(code, "carol", Now.AddMinutes(1))).Succeeded);
        Assert.True((await lobbies.JoinAsync(code, "bob", Now.AddMinutes(2))).Succeeded);

        var (report, id) = await lobbies.StartAsync(code, "alice", GameRules.Default, Now.AddMinutes(3));

        Assert.True(report.Succeeded);
        Assert.Equal("The match starts with 3 people and 1 bot.", Assert.Single(report.Lines));
        Assert.Equal(["alice", "carol", "bob", null], (await store.LoadAsync(id!.Value))!.Owners);
        Assert.Equal(id, (await lobbies.LoadAsync(code))!.GameId);
    }

    [Fact]
    public async Task Starting_twice_gives_the_same_match()
    {
        var code = await OpenAsync(2);
        var (_, first) = await lobbies.StartAsync(code, "alice", GameRules.Default, Now);

        var (again, second) = await lobbies.StartAsync(code, "alice", GameRules.Default, Now);

        Assert.True(again.Succeeded);
        Assert.Equal(first, second);
        Assert.Equal(1L, await WithDbAsync(db => db.ScalarAsync<long>("SELECT count(*) FROM games WHERE mode = 'match'")));
    }

    [Fact]
    public async Task A_full_invite_a_second_seat_and_a_started_match_refuse_a_join()
    {
        var code = await OpenAsync(2);
        await lobbies.JoinAsync(code, "bob", Now);

        var full = await lobbies.JoinAsync(code, "carol", Now);
        var twice = await lobbies.JoinAsync(code, "bob", Now);
        await lobbies.StartAsync(code, "alice", GameRules.Default, Now);
        var late = await lobbies.JoinAsync(code, "carol", Now);
        var missing = await lobbies.JoinAsync("nosuchcode", "carol", Now);

        Assert.Equal("Every seat is taken.", Assert.Single(full.Lines));
        Assert.Equal("You are already in this match.", Assert.Single(twice.Lines));
        Assert.Equal("This match has started without you.", Assert.Single(late.Lines));
        Assert.Equal("There is no such invite.", Assert.Single(missing.Lines));
        Assert.Equal(["alice", "bob"], (await lobbies.LoadAsync(code))!.Members);
    }

    [Fact]
    public async Task Only_the_host_starts_or_cancels_and_a_refusal_changes_nothing()
    {
        var code = await OpenAsync();
        await lobbies.JoinAsync(code, "bob", Now);

        var (start, id) = await lobbies.StartAsync(code, "bob", GameRules.Default, Now);
        var cancel = await lobbies.CancelAsync(code, "bob");
        var hostLeaves = await lobbies.LeaveAsync(code, "alice");

        Assert.False(start.Succeeded);
        Assert.Null(id);
        Assert.False(cancel.Succeeded);
        Assert.False(hostLeaves.Succeeded);
        var lobby = (await lobbies.LoadAsync(code))!;
        Assert.False(lobby.IsStarted);
        Assert.Equal(["alice", "bob"], lobby.Members);
        Assert.Equal(0L, await WithDbAsync(db => db.ScalarAsync<long>("SELECT count(*) FROM games")));
    }

    [Fact]
    public async Task A_player_leaves_and_the_host_cancels()
    {
        var code = await OpenAsync();
        await lobbies.JoinAsync(code, "bob", Now);

        Assert.True((await lobbies.LeaveAsync(code, "bob")).Succeeded);
        Assert.Equal(["alice"], (await lobbies.LoadAsync(code))!.Members);
        Assert.Empty(await lobbies.ListForAsync("bob"));

        Assert.True((await lobbies.CancelAsync(code, "alice")).Succeeded);
        Assert.Null(await lobbies.LoadAsync(code));
    }

    [Fact]
    public async Task A_host_has_a_few_invites_open_at_most()
    {
        for (var i = 0; i < MatchLobbies.OpenPerHost; i++)
        {
            await OpenAsync();
        }

        var (report, code) = await lobbies.OpenAsync("alice", 3, GameRules.Default, Now);
        var (tooBig, _) = await lobbies.OpenAsync("bob", 9, GameRules.Default, Now);

        Assert.False(report.Succeeded);
        Assert.Null(code);
        Assert.False(tooBig.Succeeded);
        Assert.Equal(MatchLobbies.OpenPerHost, (await lobbies.ListForAsync("alice")).Count);
        Assert.Empty(await lobbies.ListForAsync("bob"));
    }

    [Fact]
    public async Task Open_invites_are_listed_for_everyone_in_them_until_they_start()
    {
        var code = await OpenAsync();
        await lobbies.JoinAsync(code, "bob", Now);

        var mine = Assert.Single(await lobbies.ListForAsync("alice"));
        var theirs = Assert.Single(await lobbies.ListForAsync("bob"));
        Assert.Equal(new PlayerLobby(code, 3, 2, true), mine);
        Assert.False(theirs.IsHost);

        await lobbies.StartAsync(code, "alice", GameRules.Default, Now);
        Assert.Empty(await lobbies.ListForAsync("bob"));
    }
}
