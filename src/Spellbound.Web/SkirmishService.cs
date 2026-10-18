using Spellbound.Domain;
using Spellbound.Domain.Skirmish;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>A Skirmish game as a player sees it: the game, their seat and orders, and how the round stands.</summary>
/// <param name="Planning">The round being planned (the next one to play).</param>
/// <param name="LastReport">What the last round did, or null before the first one.</param>
public sealed record SkirmishView(
    long GameId,
    int Number,
    SkirmishGame Game,
    int Seat,
    IReadOnlyList<SkirmishOrder> Orders,
    bool Ready,
    int ReadyCount,
    int Players,
    int Planning,
    DateTimeOffset RoundEndsAt,
    TimeSpan RoundLength,
    SkirmishRoundReport? LastReport);

/// <summary>One of the player's Skirmish games, for the Games page.</summary>
public sealed record SkirmishSummary(
    long GameId, int Number, string SeatName, int Planning, int Rounds, bool Ready, int ReadyCount, int Players,
    DateTimeOffset RoundEndsAt, bool Ended, string Leader);

/// <summary>
/// Thin glue between the Skirmish pages and the domain (<see cref="SkirmishGame"/>), like <see cref="GameService"/> is
/// for the Realm. Each call is one transaction holding the game's row (<c>FOR UPDATE</c>), so saves, ready taps and the
/// round itself happen one at a time: two last Ready taps at once play the round once. A round plays when every player
/// still in the game is ready, or when its time runs out (<see cref="SkirmishKeeper"/>).
/// </summary>
public sealed class SkirmishService(
    HostDatabase database, TimeProvider time, IRandomSource random, PushSink push, ILogger<SkirmishService> logger)
{
    public const string Mode = "skirmish";

    private const string Busy = "The game was busy. Nothing changed, please try again.";
    private const string Moved = "That round has already played. Have a look at the new one and try again.";

    /// <summary>The time the game runs at, to whole microseconds (PostgreSQL's precision), like the Realm's.</summary>
    public DateTimeOffset Now => time.GetUtcNow() is var now ? now.AddTicks(-(now.Ticks % 10)) : default;

    /// <summary>
    /// Starts a game for these accounts, in this seat order, at once: the admin's shortcut. Players set up their own
    /// games through <see cref="SkirmishLobbyService"/>.
    /// </summary>
    public async Task<long> CreateAsync(
        IReadOnlyList<(string OwnerId, string Name, MagicSchool School)> players, SkirmishRules rules, TimeSpan roundLength)
    {
        await using var db = await database.BeginAsync();
        var hosted = await StartAsync(db, players, rules, roundLength);
        await db.CommitAsync();
        return hosted.Id;
    }

    /// <summary>Starts a game in the caller's transaction, which commits it (a lobby starts its game as the last player joins).</summary>
    internal async Task<HostedGame> StartAsync(
        DbSession db, IReadOnlyList<(string OwnerId, string Name, MagicSchool School)> players, SkirmishRules rules, TimeSpan roundLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players.Count, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(roundLength, TimeSpan.Zero);
        var now = Now;
        var seed = (int)(random.NextDouble() * int.MaxValue);
        var game = SkirmishGame.Start([.. players.Select(p => (p.Name, p.School))], rules, new SeededRandom(seed));

        await db.LockGamesOfModeAsync(Mode);
        var hosted = await db.InsertGameAsync(new NewGame(Mode, roundLength * rules.Rounds, DomainJson.Serialize(rules)), now);
        await db.ExecuteAsync(
            "INSERT INTO skirmish_games (game_id, seed, state, round_length, round_ends_at) VALUES ($1, $2, $3, $4, $5)",
            hosted.Id, seed, new Jsonb(DomainJson.Serialize(game)), roundLength, now + roundLength);
        for (var seat = 0; seat < players.Count; seat++)
        {
            await db.ExecuteAsync(
                "INSERT INTO skirmish_seats (game_id, seat, owner_id) VALUES ($1, $2, $3)", hosted.Id, seat, players[seat].OwnerId);
        }

        logger.LogInformation("Skirmish {Number} starts with {Players} players.", hosted.Round.Number, players.Count);
        return hosted;
    }

    /// <summary>
    /// Starts a game for the accounts with these emails, in this order (the admin page's form). Each player plays under
    /// their Realm mage's name and school this round, or else the start of their email and a school in turn. Returns the
    /// game's id and number (what players see: "Skirmish 3"), or why it can't start.
    /// </summary>
    public async Task<(long? GameId, int Number, string? Problem)> CreateForEmailsAsync(
        IReadOnlyList<string> emails, TimeSpan roundLength, AccountService accounts, SkirmishRules? rules = null)
    {
        rules ??= SkirmishRules.Default;
        var cleaned = emails.Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
        if (cleaned.Count < 2 || cleaned.Count > 6)
        {
            return (null, 0, "A Skirmish needs 2–6 players: one email per line.");
        }

        if (cleaned.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cleaned.Count)
        {
            return (null, 0, "Each player can only take one seat.");
        }

        if (roundLength <= TimeSpan.Zero)
        {
            return (null, 0, "Give each round some time.");
        }

        var players = new List<(string OwnerId, string Name, MagicSchool School)>();
        await using (var db = await database.BeginAsync())
        {
            var realm = await db.LoadRunningGameAsync(SpellboundHost.RealmMode);
            foreach (var email in cleaned)
            {
                if (await accounts.FindByEmailAsync(email) is not { } account)
                {
                    return (null, 0, $"No account has the email {email}: they need to sign in once first.");
                }

                if (await db.IsBannedAsync(account.Id))
                {
                    return (null, 0, $"{email} is banned.");
                }

                var mage = realm is null ? null : await db.LoadMageAsync(realm.Id, account.Id);
                players.Add((account.Id, mage?.Name ?? email.Split('@')[0], mage?.School ?? (MagicSchool)(players.Count % 5)));
            }
        }

        var id = await CreateAsync(players, rules with { Players = players.Count }, roundLength);
        await using var read = await database.BeginAsync();
        return (id, (await read.LoadGameAsync(id))!.Round.Number, null);
    }

    /// <summary>The player's Skirmish games, running first, then ended ones, newest first.</summary>
    public async Task<IReadOnlyList<SkirmishSummary>> GetMineAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        var ids = await db.QueryAsync(
            "SELECT s.game_id FROM skirmish_seats s JOIN skirmish_games g ON g.game_id = s.game_id WHERE s.owner_id = $1 ORDER BY g.ended, g.game_id DESC",
            r => r.GetInt64(0),
            ownerId);
        var bans = await db.LoadBansAsync();
        var summaries = new List<SkirmishSummary>();
        foreach (var id in ids)
        {
            var row = (await LoadAsync(db, id, forUpdate: false))!;
            var seat = row.SeatOf(ownerId)!;
            var playing = Playing(row, bans);
            summaries.Add(new SkirmishSummary(
                id, row.Number, row.Game.Seats[seat.Seat].Name, row.Game.Round + 1, row.Game.Rules.Rounds, seat.Ready,
                playing.Count(s => s.Ready), playing.Count, row.RoundEndsAt, row.Game.Ended, row.Game.Standings()[0].Name));
        }

        return summaries;
    }

    /// <summary>The game as the player sees it, or null if they don't sit in it.</summary>
    public async Task<SkirmishView?> GetAsync(long gameId, string ownerId)
    {
        await using var db = await database.BeginAsync();
        if (await LoadAsync(db, gameId, forUpdate: false) is not { } row || row.SeatOf(ownerId) is not { } seat)
        {
            return null;
        }

        var report = await db.SingleAsync(
            "SELECT report::text FROM skirmish_rounds WHERE game_id = $1 ORDER BY round DESC LIMIT 1",
            r => DomainJson.Deserialize<SkirmishRoundReport>(r.GetString(0)),
            gameId);
        var playing = Playing(row, await db.LoadBansAsync());
        return new SkirmishView(
            gameId, row.Number, row.Game, seat.Seat, seat.Orders, seat.Ready, playing.Count(s => s.Ready),
            playing.Count, row.Game.Round + 1, row.RoundEndsAt, row.RoundLength, report);
    }

    /// <summary>
    /// Saves the player's orders for the round they planned (<paramref name="round"/>), replacing earlier ones. Returns
    /// why they can't be saved, or null. Orders are checked as the round would play them if nobody got in the way.
    /// </summary>
    public Task<string?> SaveOrdersAsync(long gameId, string ownerId, int round, IReadOnlyList<SkirmishOrder> orders) =>
        ActAsync(gameId, ownerId, round, async (db, row, seat, _) =>
        {
            if (seat.Ready)
            {
                return "You're ready: take back Ready to change your orders.";
            }

            if (row.Game.CheckOrders(seat.Seat, orders) is { } problem)
            {
                return problem;
            }

            await db.ExecuteAsync(
                "UPDATE skirmish_seats SET orders = $3 WHERE game_id = $1 AND seat = $2",
                gameId, seat.Seat, new Jsonb(DomainJson.Serialize(orders.ToList())));
            return null;
        });

    /// <summary>
    /// Readies the player up (or takes it back). When the last player still in the game is ready, the round plays at
    /// once, in this same transaction.
    /// </summary>
    public Task<string?> SetReadyAsync(long gameId, string ownerId, int round, bool ready) =>
        ActAsync(gameId, ownerId, round, async (db, row, seat, notifications) =>
        {
            await db.ExecuteAsync("UPDATE skirmish_seats SET ready = $3 WHERE game_id = $1 AND seat = $2", gameId, seat.Seat, ready);
            var playing = Playing(row, await db.LoadBansAsync());
            var waiting = playing.Where(s => s.Seat != seat.Seat && !s.Ready).ToList();
            if (ready && waiting.Count == 0)
            {
                await PlayAsync(db, row with { Seats = [.. row.Seats.Select(s => s.Seat == seat.Seat ? s with { Ready = true } : s)] }, notifications, seat.Seat);
            }
            else if (ready && waiting is [{ Nudged: false } last])
            {
                // Everyone else is ready: tell the last one, once a round.
                await db.ExecuteAsync("UPDATE skirmish_seats SET nudged = true WHERE game_id = $1 AND seat = $2", gameId, last.Seat);
                notifications.Add(Message(row, last.OwnerId!, $"Everyone else is ready in Skirmish {row.Number}", $"Round {row.Game.Round + 1} plays when you are."));
            }

            return null;
        });

    /// <summary>Plays every round whose time has run out. Returns how many played. Safe on several hosts at once.</summary>
    public async Task<int> PlayDueAsync(CancellationToken cancellationToken = default)
    {
        var played = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var db = await database.BeginAsync(cancellationToken);
            var due = await db.ScalarAsync<long?>(
                "SELECT game_id FROM skirmish_games WHERE NOT ended AND round_ends_at <= $1 ORDER BY round_ends_at LIMIT 1 FOR UPDATE SKIP LOCKED",
                Now);
            if (due is not { } gameId || await LoadAsync(db, gameId, forUpdate: true) is not { } row)
            {
                return played;
            }

            var notifications = new List<PushMessage>();
            await PlayAsync(db, row, notifications, playedBy: null);
            await db.CommitAsync();
            Notify(notifications);
            played++;
        }

        return played;
    }

    /// <summary>
    /// Reminds players who aren't ready that a round's time is running out: once a round, when what's left is under an
    /// hour or a tenth of the round, whichever is shorter. Returns how many games were reminded.
    /// </summary>
    public async Task<int> RemindDueAsync(CancellationToken cancellationToken = default)
    {
        var reminded = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var db = await database.BeginAsync(cancellationToken);
            var due = await db.ScalarAsync<long?>(
                """
                SELECT g.game_id FROM skirmish_games g
                WHERE NOT g.ended AND g.round_ends_at > $1
                  AND g.round_ends_at - least(interval '1 hour', g.round_length / 10) <= $1
                  AND g.reminded_round <= (g.state ->> 'round')::int
                ORDER BY g.round_ends_at LIMIT 1 FOR UPDATE SKIP LOCKED
                """,
                Now);
            if (due is not { } gameId || await LoadAsync(db, gameId, forUpdate: true) is not { } row)
            {
                return reminded;
            }

            var planning = row.Game.Round + 1;
            await db.ExecuteAsync("UPDATE skirmish_games SET reminded_round = $2 WHERE game_id = $1", gameId, planning);
            var notifications = Playing(row, await db.LoadBansAsync())
                .Where(s => !s.Ready)
                .Select(s => Message(row, s.OwnerId!, $"Time is running out in Skirmish {row.Number}", $"Round {planning} plays soon, ready or not."))
                .ToList();
            await db.CommitAsync();
            Notify(notifications);
            reminded++;
        }

        return reminded;
    }

    /// <summary>
    /// Plays the round: the saved orders, who was ready, the luck recorded; then the next round starts. Everyone still
    /// playing hears about it, except the player whose tap played it (<paramref name="playedBy"/>), who is looking at it.
    /// </summary>
    private async Task PlayAsync(DbSession db, GameRow row, List<PushMessage> notifications, int? playedBy)
    {
        var now = Now;
        var orders = row.Seats.Where(s => s.Orders.Count > 0).ToDictionary(s => s.Seat, s => s.Orders);
        var ready = row.Seats.Where(s => s.Ready && s.OwnerId is not null).Select(s => s.Seat).ToHashSet();
        var luck = new RecordingRandom(random);
        var report = row.Game.PlayRound(orders, luck, ready);

        await db.ExecuteAsync(
            """
            INSERT INTO skirmish_rounds (game_id, round, played_at, orders, ready, draws, report, state_after, rules_hash)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """,
            row.GameId, report.Round, now, new Jsonb(DomainJson.Serialize(orders)), new Jsonb(DomainJson.Serialize(ready.Order())),
            new Jsonb(DomainJson.Serialize(luck.Draws)), new Jsonb(DomainJson.Serialize(report)),
            new Jsonb(DomainJson.Serialize(row.Game)), ActionLog.RulesHash(row.Game.Rules));
        await db.ExecuteAsync(
            "UPDATE skirmish_games SET state = $2, round_ends_at = $3, ended = $4 WHERE game_id = $1",
            row.GameId, new Jsonb(DomainJson.Serialize(row.Game)), now + row.RoundLength, row.Game.Ended);
        await db.ExecuteAsync("UPDATE skirmish_seats SET orders = '[]', ready = false, nudged = false WHERE game_id = $1", row.GameId);
        if (row.Game.Ended)
        {
            await db.EndGameAsync(row.GameId, now);
            await RecordResultsAsync(db, row, now);
            logger.LogInformation("Skirmish {Number} ended after round {Round}.", row.Number, report.Round);
        }

        var winner = row.Game.Standings()[0];
        foreach (var seat in Playing(row, await db.LoadBansAsync()).Where(s => s.Seat != playedBy))
        {
            notifications.Add(row.Game.Ended
                ? Message(row, seat.OwnerId!, $"Skirmish {row.Number} is over", $"{winner.Name} wins with {row.Game.Score(winner.Index).Total} points.")
                : Message(row, seat.OwnerId!, $"Round {report.Round} played in Skirmish {row.Number}", $"Plan round {report.Round + 1}."));
        }
    }

    /// <summary>
    /// The win record: a row for every seat that still has an account, out or not (dropping out counts as a game played
    /// and lost). The winner is the top of the standings, as the round report announced.
    /// </summary>
    private static async Task RecordResultsAsync(DbSession db, GameRow row, DateTimeOffset now)
    {
        var standings = row.Game.Standings();
        for (var place = 0; place < standings.Count; place++)
        {
            var seat = standings[place];
            if (row.Seats.Single(s => s.Seat == seat.Index).OwnerId is not { } ownerId)
            {
                continue;
            }

            await db.ExecuteAsync(
                """
                INSERT INTO skirmish_results (game_id, owner_id, seat, name, place, points, won, ended_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
                """,
                row.GameId, ownerId, seat.Index, seat.Name, place + 1, row.Game.Score(seat.Index).Total, place == 0, now);
        }
    }

    private static PushMessage Message(GameRow row, string ownerId, string title, string body) =>
        new(ownerId, title, body, $"/skirmish/{row.GameId}");

    private void Notify(IEnumerable<PushMessage> notifications)
    {
        foreach (var notification in notifications)
        {
            push.Send(notification);
        }
    }

    /// <summary>
    /// The seats the round waits for: with an owner who isn't banned, and not out of the game. When all of them are
    /// ready, the round plays.
    /// </summary>
    private static List<SeatRow> Playing(GameRow row, BanList bans) =>
        [.. row.Seats.Where(s => s.OwnerId is { } owner && !bans.IsBanned(owner) && !row.Game.Seats[s.Seat].Out)];

    /// <summary>Runs a change for a player in their game, in its own transaction, if they may act and the round still stands.</summary>
    private async Task<string?> ActAsync(
        long gameId, string ownerId, int round, Func<DbSession, GameRow, SeatRow, List<PushMessage>, Task<string?>> action)
    {
        var notifications = new List<PushMessage>();
        try
        {
            await using var db = await database.BeginAsync();
            if (await LoadAsync(db, gameId, forUpdate: true) is not { } row || row.SeatOf(ownerId) is not { } seat)
            {
                return "You're not in that game.";
            }

            if (await db.IsBannedAsync(ownerId))
            {
                return "Your account is banned.";
            }

            if (row.Game.Ended)
            {
                return "The game is over.";
            }

            if (row.Game.Seats[seat.Seat].Out)
            {
                return "You're out of this game.";
            }

            if (round != row.Game.Round + 1)
            {
                return Moved;
            }

            if (await action(db, row, seat, notifications) is { } problem)
            {
                return problem;
            }

            await db.CommitAsync();
            Notify(notifications); // only once the change is saved
            return null;
        }
        catch (Exception ex) when (HostStore.IsBusy(ex))
        {
            return Busy;
        }
    }

    private static async Task<GameRow?> LoadAsync(DbSession db, long gameId, bool forUpdate)
    {
        var game = (await db.QueryAsync(
            "SELECT h.number, g.state::text, g.round_length, g.round_ends_at FROM skirmish_games g JOIN games h ON h.id = g.game_id WHERE g.game_id = $1"
            + (forUpdate ? " FOR UPDATE OF g" : ""),
            r => (Number: r.GetInt32(0), State: r.GetString(1), Length: r.GetFieldValue<TimeSpan>(2), EndsAt: r.GetFieldValue<DateTimeOffset>(3)),
            gameId)).FirstOrDefault();
        if (game.State is null)
        {
            return null;
        }

        var seats = await db.QueryAsync(
            "SELECT seat, owner_id, orders::text, ready, nudged FROM skirmish_seats WHERE game_id = $1 ORDER BY seat",
            r => new SeatRow(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), DomainJson.Deserialize<List<SkirmishOrder>>(r.GetString(2)), r.GetBoolean(3), r.GetBoolean(4)),
            gameId);
        return new GameRow(gameId, game.Number, DomainJson.Deserialize<SkirmishGame>(game.State), game.Length, game.EndsAt, seats);
    }

    private sealed record SeatRow(int Seat, string? OwnerId, IReadOnlyList<SkirmishOrder> Orders, bool Ready, bool Nudged);

    private sealed record GameRow(long GameId, int Number, SkirmishGame Game, TimeSpan RoundLength, DateTimeOffset RoundEndsAt, IReadOnlyList<SeatRow> Seats)
    {
        public SeatRow? SeatOf(string ownerId) => Seats.FirstOrDefault(s => s.OwnerId == ownerId);
    }
}
