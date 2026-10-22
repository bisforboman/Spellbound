using System.Security.Cryptography;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>An invite as it is loaded: its host, its size, who has joined (in seat order, the host first) and its match
/// once started.</summary>
public sealed record Lobby(string Code, string HostId, int Seats, IReadOnlyList<string> Members, long? GameId)
{
    public bool IsStarted => GameId is not null;

    public int Free => Seats - Members.Count;
}

/// <summary>An open invite a player is in, for their list.</summary>
public sealed record PlayerLobby(string Code, int Seats, int Joined, bool IsHost);

/// <summary>
/// Invites to a match (docs/discussions/playing-with-friends.md): a host opens a lobby and shares its link, people join
/// it, and the host starts the match, with bots in the seats nobody took. Each change locks the lobby's row, so two
/// people can't take the last seat and a start can't race a join.
/// </summary>
public sealed class MatchLobbies(HostDatabase database)
{
    /// <summary>How many invites a host may have open (not started) at once.</summary>
    public const int OpenPerHost = 3;

    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>Opens an invite for a match of <paramref name="seats"/> seats, with the host in the first; returns its
    /// code, or a failed report.</summary>
    public async Task<(Report Report, string? Code)> OpenAsync(string hostId, int seats, GameRules rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (seats < rules.MinPlayers || seats > rules.MaxPlayers)
        {
            return (Report.Fail($"A match has {rules.MinPlayers} to {rules.MaxPlayers} players."), null);
        }

        await using var db = await database.BeginAsync();
        await db.ExecuteAsync("SELECT 1 FROM accounts WHERE id = $1 FOR UPDATE", hostId); // one host's opens take turns
        var open = await db.ScalarAsync<long>("SELECT count(*) FROM match_lobbies WHERE host_id = $1 AND game_id IS NULL", hostId);
        if (open >= OpenPerHost)
        {
            return (Report.Fail($"You already have {OpenPerHost} invites open. Start or cancel one first."), null);
        }

        var code = RandomNumberGenerator.GetString(Alphabet, 16);
        await db.ExecuteAsync("INSERT INTO match_lobbies (code, host_id, seats, created_at) VALUES ($1, $2, $3, $4)", code, hostId, seats, now);
        await db.ExecuteAsync("INSERT INTO match_lobby_members (code, owner_id, joined_at) VALUES ($1, $2, $3)", code, hostId, now);
        await db.CommitAsync();
        return (new Report(true, [$"Invite opened for {seats} players."]), code);
    }

    /// <summary>An invite by its code, or null.</summary>
    public async Task<Lobby?> LoadAsync(string code)
    {
        await using var db = await database.BeginAsync();
        return await LoadAsync(db, code, forUpdate: false);
    }

    /// <summary>Takes a free seat in an open invite. Changes nothing if it has started, is full, or the player is in.</summary>
    public Task<Report> JoinAsync(string code, string ownerId, DateTimeOffset now) => ChangeAsync(code, async (db, lobby) =>
    {
        if (lobby.Members.Contains(ownerId))
        {
            return Report.Fail("You are already in this match.");
        }

        if (lobby.IsStarted)
        {
            return Report.Fail("This match has started without you.");
        }

        if (lobby.Free == 0)
        {
            return Report.Fail("Every seat is taken.");
        }

        await db.ExecuteAsync("INSERT INTO match_lobby_members (code, owner_id, joined_at) VALUES ($1, $2, $3)", code, ownerId, now);
        return new Report(true, [$"You took seat {lobby.Members.Count + 1}."]);
    });

    /// <summary>Gives up a seat in an invite that hasn't started. The host can't leave; they cancel instead.</summary>
    public Task<Report> LeaveAsync(string code, string ownerId) => ChangeAsync(code, async (db, lobby) =>
    {
        if (lobby.IsStarted || !lobby.Members.Contains(ownerId))
        {
            return Report.Fail("You have no seat to give up here.");
        }

        if (lobby.HostId == ownerId)
        {
            return Report.Fail("You are the host: cancel the invite instead.");
        }

        await db.ExecuteAsync("DELETE FROM match_lobby_members WHERE code = $1 AND owner_id = $2", code, ownerId);
        return new Report(true, ["You left the match."]);
    });

    /// <summary>The host cancels an invite that hasn't started.</summary>
    public Task<Report> CancelAsync(string code, string ownerId) => ChangeAsync(code, async (db, lobby) =>
    {
        if (lobby.HostId != ownerId || lobby.IsStarted)
        {
            return Report.Fail("Only the host can cancel an invite, before it starts.");
        }

        await db.ExecuteAsync("DELETE FROM match_lobbies WHERE code = $1", code);
        return new Report(true, ["Invite cancelled."]);
    });

    /// <summary>
    /// The host starts the match: the members in seat order, then bots in the free seats. The match and the lobby's
    /// link to it are saved together. Returns the match's id (the same one if it had already started), or a failed report.
    /// </summary>
    public async Task<(Report Report, long? GameId)> StartAsync(string code, string ownerId, GameRules rules, DateTimeOffset now)
    {
        long? started = null;
        var report = await ChangeAsync(code, async (db, lobby) =>
        {
            if (lobby.HostId != ownerId)
            {
                return Report.Fail("Only the host can start the match.");
            }

            if (lobby.GameId is { } already)
            {
                started = already;
                return new Report(true, ["The match has already started."]);
            }

            var owners = lobby.Members.Cast<string?>().Concat(Enumerable.Repeat<string?>(null, lobby.Free)).ToList();
            started = await MatchStore.CreateAsync(db, owners, rules, now);
            await db.ExecuteAsync("UPDATE match_lobbies SET game_id = $2 WHERE code = $1", code, started.Value);
            return new Report(true, [$"The match starts with {lobby.Members.Count} {(lobby.Members.Count == 1 ? "person" : "people")} and {lobby.Free} {(lobby.Free == 1 ? "bot" : "bots")}."]);
        });

        return (report, report.Succeeded ? started : null);
    }

    /// <summary>The invites a player is in that haven't started, newest first.</summary>
    public async Task<IReadOnlyList<PlayerLobby>> ListForAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        return await db.QueryAsync(
            """
            SELECT l.code, l.seats, (SELECT count(*) FROM match_lobby_members c WHERE c.code = l.code), l.host_id = $1
            FROM match_lobbies l JOIN match_lobby_members m ON m.code = l.code AND m.owner_id = $1
            WHERE l.game_id IS NULL
            ORDER BY l.created_at DESC
            """,
            r => new PlayerLobby(r.GetString(0), r.GetInt32(1), (int)r.GetInt64(2), r.GetBoolean(3)),
            ownerId);
    }

    /// <summary>A change to an invite with its row locked: saved only if it succeeds.</summary>
    private async Task<Report> ChangeAsync(string code, Func<DbSession, Lobby, Task<Report>> change)
    {
        await using var db = await database.BeginAsync();
        if (await LoadAsync(db, code, forUpdate: true) is not { } lobby)
        {
            return Report.Fail("There is no such invite.");
        }

        var report = await change(db, lobby);
        if (report.Succeeded)
        {
            await db.CommitAsync();
        }

        return report;
    }

    private static async Task<Lobby?> LoadAsync(DbSession db, string code, bool forUpdate)
    {
        if (await db.SingleAsync(
                "SELECT host_id, seats, game_id FROM match_lobbies WHERE code = $1" + (forUpdate ? " FOR UPDATE" : ""),
                r => new Lobby(code, r.GetString(0), r.GetInt32(1), [], r.IsDBNull(2) ? null : r.GetInt64(2)),
                code) is not { } lobby)
        {
            return null;
        }

        var members = await db.QueryAsync(
            "SELECT owner_id FROM match_lobby_members WHERE code = $1 ORDER BY owner_id = $2 DESC, joined_at, owner_id",
            r => r.GetString(0), code, lobby.HostId);
        return lobby with { Members = members };
    }
}
