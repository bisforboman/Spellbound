using System.Security.Cryptography;
using Spellbound.Domain;
using Spellbound.Domain.Skirmish;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>Server settings for Skirmish (configuration section <c>"Skirmish"</c>), apart from the rules of a game.</summary>
public sealed class SkirmishOptions
{
    /// <summary>How many Skirmishes a player can be in at once, counting running games and games waiting for players.</summary>
    public int MaxGamesPerPlayer { get; set; } = 3;
}

/// <summary>Someone who has joined a lobby, under the name and school they'll play as.</summary>
public sealed record LobbyPlayer(string OwnerId, string Name, MagicSchool School);

/// <summary>A Skirmish set up by a player and waiting for players, or (once <see cref="GameId"/> is set) started.</summary>
/// <param name="Rules">The rules chosen; <c>Players</c> is how many seats it waits for.</param>
public sealed record SkirmishLobby(
    long Id, string Code, string? CreatorId, SkirmishRules Rules, TimeSpan RoundLength, bool Public, DateTimeOffset CreatedAt,
    IReadOnlyList<LobbyPlayer> Players, long? GameId)
{
    public bool Started => GameId is not null;

    public bool Has(string ownerId) => Players.Any(p => p.OwnerId == ownerId);
}

/// <summary>
/// Players setting up and joining their own Skirmishes. A lobby is shared by its invite code (or listed, if public);
/// it starts its game when the last seat fills, in the same transaction as that join, or when its creator starts it
/// with whoever has joined. Each call holds the lobby's row (<c>FOR UPDATE</c>), so two players can't take the last
/// seat at once, and a per-player lock so nobody slips past the cap on games by joining two lobbies at once.
/// </summary>
public sealed class SkirmishLobbyService(
    HostDatabase database, SkirmishService skirmishes, SkirmishOptions options, PushSink push, ILogger<SkirmishLobbyService> logger)
{
    public static readonly TimeSpan MinRoundLength = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxRoundLength = TimeSpan.FromDays(7);

    /// <summary>Invite code letters: no 0/O, 1/I/L, so a code read aloud or typed from a screen works.</summary>
    private const string CodeLetters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;
    private const string Busy = "The game was busy. Nothing changed, please try again.";

    /// <summary>
    /// Sets up a game and seats its creator in it. Returns the invite code, or why it can't be set up.
    /// </summary>
    public async Task<(string? Code, string? Problem)> CreateAsync(
        string ownerId, string name, MagicSchool school, SkirmishRules rules, TimeSpan roundLength, bool isPublic)
    {
        if ((rules.CreatorProblem() ?? Mage.NameProblem(name) ?? RoundLengthProblem(roundLength)) is { } problem)
        {
            return (null, problem);
        }

        try
        {
            await using var db = await database.BeginAsync();
            if (await PlayerProblemAsync(db, ownerId) is { } refused)
            {
                return (null, refused);
            }

            var now = skirmishes.Now;
            for (var attempt = 0; ; attempt++)
            {
                var code = RandomNumberGenerator.GetString(CodeLetters, CodeLength);
                if (await db.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM skirmish_lobbies WHERE code = $1)", code) && attempt < 10)
                {
                    continue; // about one in a billion per open game; try another
                }

                var id = await db.ScalarAsync<long>(
                    """
                    INSERT INTO skirmish_lobbies (code, creator_id, rules, round_length, public, created_at)
                    VALUES ($1, $2, $3, $4, $5, $6) RETURNING id
                    """,
                    code, ownerId, new Jsonb(DomainJson.Serialize(rules)), roundLength, isPublic, now);
                await db.ExecuteAsync(
                    "INSERT INTO skirmish_lobby_players (lobby_id, owner_id, name, school, joined_at) VALUES ($1, $2, $3, $4, $5)",
                    id, ownerId, name.Trim(), school.ToString(), now);
                await db.CommitAsync();
                logger.LogInformation("Skirmish lobby {Code} set up for {Players} players.", code, rules.Players);
                return (code, null);
            }
        }
        catch (Exception ex) when (HostStore.IsBusy(ex))
        {
            return (null, Busy);
        }
    }

    /// <summary>The lobby with this code (typed in any case, with spaces), or null.</summary>
    public async Task<SkirmishLobby?> FindAsync(string code)
    {
        await using var db = await database.BeginAsync();
        return await LoadAsync(db, Normalise(code), forUpdate: false);
    }

    /// <summary>Public lobbies still waiting for players, newest first.</summary>
    public async Task<IReadOnlyList<SkirmishLobby>> ListOpenAsync(int limit = 20)
    {
        await using var db = await database.BeginAsync();
        var codes = await db.QueryAsync(
            "SELECT code FROM skirmish_lobbies WHERE public AND game_id IS NULL ORDER BY created_at DESC LIMIT $1",
            r => r.GetString(0),
            limit);
        return await LoadAllAsync(db, codes);
    }

    /// <summary>The lobbies the player has joined that haven't started, oldest first.</summary>
    public async Task<IReadOnlyList<SkirmishLobby>> GetMineAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        var codes = await db.QueryAsync(
            """
            SELECT l.code FROM skirmish_lobbies l JOIN skirmish_lobby_players p ON p.lobby_id = l.id
            WHERE p.owner_id = $1 AND l.game_id IS NULL ORDER BY l.created_at
            """,
            r => r.GetString(0),
            ownerId);
        return await LoadAllAsync(db, codes);
    }

    /// <summary>A name and school to suggest: the player's Realm mage's, if they have one this round.</summary>
    public async Task<(string Name, MagicSchool School)> SuggestAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        var realm = await db.LoadRunningGameAsync(SpellboundHost.RealmMode);
        var mage = realm is null ? null : await db.LoadMageAsync(realm.Id, ownerId);
        return mage is null ? (string.Empty, MagicSchool.A) : (mage.Name, mage.School);
    }

    /// <summary>
    /// Takes a seat in the lobby. If that fills it, the game starts at once and everyone else waiting hears about it.
    /// Returns why the player can't join, or null.
    /// </summary>
    public Task<string?> JoinAsync(string code, string ownerId, string name, MagicSchool school) =>
        ActAsync(code, ownerId, async (db, lobby, notifications) =>
        {
            if (lobby.Has(ownerId))
            {
                return "You've already joined this game.";
            }

            if (lobby.Players.Count >= lobby.Rules.Players)
            {
                return "This game is full.";
            }

            if ((Mage.NameProblem(name) ?? await PlayerProblemAsync(db, ownerId)) is { } problem)
            {
                return problem;
            }

            if (lobby.Players.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return $"Someone in this game is already called {name.Trim()}.";
            }

            await db.ExecuteAsync(
                "INSERT INTO skirmish_lobby_players (lobby_id, owner_id, name, school, joined_at) VALUES ($1, $2, $3, $4, $5)",
                lobby.Id, ownerId, name.Trim(), school.ToString(), skirmishes.Now);
            var players = lobby.Players.Append(new LobbyPlayer(ownerId, name.Trim(), school)).ToList();
            if (players.Count == lobby.Rules.Players)
            {
                await StartAsync(db, lobby, players, ownerId, notifications);
            }

            return null;
        });

    /// <summary>Leaves a lobby before it starts. When its creator leaves, the game is called off for everyone.</summary>
    public Task<string?> LeaveAsync(string code, string ownerId) =>
        ActAsync(code, ownerId, async (db, lobby, notifications) =>
        {
            if (!lobby.Has(ownerId))
            {
                return "You're not in this game.";
            }

            if (lobby.CreatorId == ownerId)
            {
                await db.ExecuteAsync("DELETE FROM skirmish_lobbies WHERE id = $1", lobby.Id);
                notifications.AddRange(lobby.Players.Where(p => p.OwnerId != ownerId).Select(p =>
                    new PushMessage(p.OwnerId, "A Skirmish was called off", "Its creator called it off before it started.", "/")));
            }
            else
            {
                await db.ExecuteAsync("DELETE FROM skirmish_lobby_players WHERE lobby_id = $1 AND owner_id = $2", lobby.Id, ownerId);
            }

            return null;
        });

    /// <summary>The creator starts the game with whoever has joined (at least two), without waiting for it to fill.</summary>
    public Task<string?> StartNowAsync(string code, string ownerId) =>
        ActAsync(code, ownerId, async (db, lobby, notifications) =>
        {
            if (lobby.CreatorId != ownerId)
            {
                return "Only the player who set up the game can start it early.";
            }

            if (lobby.Players.Count < SkirmishRules.MinPlayers)
            {
                return $"A Skirmish needs at least {SkirmishRules.MinPlayers} players.";
            }

            await StartAsync(db, lobby, lobby.Players, ownerId, notifications);
            return null;
        });

    /// <summary>Why the player can't set up or join another game, or null: a ban, or the cap on games at once.</summary>
    private async Task<string?> PlayerProblemAsync(DbSession db, string ownerId)
    {
        if (await db.IsBannedAsync(ownerId))
        {
            return "Your account is banned.";
        }

        // Held until the transaction ends, so two joins by one player at once count each other.
        await db.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext('skirmish-player:' || $1))", ownerId);
        var games = await db.ScalarAsync<long>(
            """
            SELECT (SELECT count(*) FROM skirmish_seats s JOIN skirmish_games g ON g.game_id = s.game_id
                    WHERE s.owner_id = $1 AND NOT g.ended)
                 + (SELECT count(*) FROM skirmish_lobby_players p JOIN skirmish_lobbies l ON l.id = p.lobby_id
                    WHERE p.owner_id = $1 AND l.game_id IS NULL)
            """,
            ownerId);
        return games >= options.MaxGamesPerPlayer
            ? $"You're in {games} Skirmishes already, the most at once. Finish or leave one first."
            : null;
    }

    private static string? RoundLengthProblem(TimeSpan roundLength) =>
        roundLength < MinRoundLength || roundLength > MaxRoundLength ? "A round lasts between a minute and a week." : null;

    /// <summary>Starts the lobby's game, seats in the order players joined, and tells everyone but the player who started it.</summary>
    private async Task StartAsync(
        DbSession db, SkirmishLobby lobby, IReadOnlyList<LobbyPlayer> players, string startedBy, List<PushMessage> notifications)
    {
        var hosted = await skirmishes.StartAsync(
            db, [.. players.Select(p => (p.OwnerId, p.Name, p.School))], lobby.Rules with { Players = players.Count }, lobby.RoundLength);
        await db.ExecuteAsync("UPDATE skirmish_lobbies SET game_id = $2 WHERE id = $1", lobby.Id, hosted.Id);
        await db.ExecuteAsync("DELETE FROM skirmish_lobby_players WHERE lobby_id = $1", lobby.Id);
        notifications.AddRange(players.Where(p => p.OwnerId != startedBy).Select(p =>
            new PushMessage(p.OwnerId, $"Skirmish {hosted.Round.Number} has started", "Plan your first round.", $"/skirmish/{hosted.Id}")));
    }

    /// <summary>Runs a change to a lobby that hasn't started, in one transaction, and sends its notifications once it's saved.</summary>
    private async Task<string?> ActAsync(
        string code, string ownerId, Func<DbSession, SkirmishLobby, List<PushMessage>, Task<string?>> action)
    {
        var notifications = new List<PushMessage>();
        try
        {
            await using var db = await database.BeginAsync();
            if (await LoadAsync(db, Normalise(code), forUpdate: true) is not { } lobby)
            {
                return "No game has that code.";
            }

            if (lobby.Started)
            {
                return "That game has already started.";
            }

            if (await action(db, lobby, notifications) is { } problem)
            {
                return problem;
            }

            await db.CommitAsync();
            foreach (var notification in notifications)
            {
                push.Send(notification); // only once the change is saved
            }

            return null;
        }
        catch (Exception ex) when (HostStore.IsBusy(ex))
        {
            return Busy;
        }
    }

    private static string Normalise(string code) => new([.. code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static async Task<IReadOnlyList<SkirmishLobby>> LoadAllAsync(DbSession db, IEnumerable<string> codes)
    {
        var lobbies = new List<SkirmishLobby>();
        foreach (var code in codes)
        {
            lobbies.Add((await LoadAsync(db, code, forUpdate: false))!);
        }

        return lobbies;
    }

    private static async Task<SkirmishLobby?> LoadAsync(DbSession db, string code, bool forUpdate)
    {
        var row = (await db.QueryAsync(
            "SELECT id, creator_id, rules::text, round_length, public, created_at, game_id FROM skirmish_lobbies WHERE code = $1"
            + (forUpdate ? " FOR UPDATE" : ""),
            r => (Id: r.GetInt64(0), Creator: r.IsDBNull(1) ? null : r.GetString(1), Rules: r.GetString(2), Length: r.GetFieldValue<TimeSpan>(3),
                Public: r.GetBoolean(4), CreatedAt: r.GetFieldValue<DateTimeOffset>(5), GameId: r.IsDBNull(6) ? (long?)null : r.GetInt64(6)),
            code)).FirstOrDefault();
        if (row.Rules is null)
        {
            return null;
        }

        var players = await db.QueryAsync(
            "SELECT owner_id, name, school FROM skirmish_lobby_players WHERE lobby_id = $1 ORDER BY joined_at, owner_id",
            r => new LobbyPlayer(r.GetString(0), r.GetString(1), Enum.Parse<MagicSchool>(r.GetString(2))),
            row.Id);
        return new SkirmishLobby(
            row.Id, code, row.Creator, DomainJson.Deserialize<SkirmishRules>(row.Rules), row.Length, row.Public, row.CreatedAt, players, row.GameId);
    }
}
