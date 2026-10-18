using Npgsql;
using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>The hosting side of the database: connection, schema, and the queries several host services share.</summary>
public static class HostStore
{
    /// <summary>The connection string's name (ConnectionStrings:Spellbound, or ConnectionStrings__Spellbound).</summary>
    public const string ConnectionStringName = "Spellbound";

    public static string ConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName) is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException(
                $"No database is configured. Set ConnectionStrings__{ConnectionStringName} to a PostgreSQL connection string.");

    /// <summary>The host's tables (Hosting/Sql), migrated before any game's.</summary>
    public static SchemaModule Schema { get; } =
        SchemaModule.FromResources("host", typeof(HostStore).Assembly, "Spellbound.Web.Hosting.Sql.");

    /// <summary>A statement that broke a unique key, such as a second row for the same key.</summary>
    public static bool IsDuplicateKey(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>A statement that waited too long for a row another action had locked, or a deadlock.</summary>
    public static bool IsBusy(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected };

    /// <summary>
    /// The running game of a mode (the newest one not yet wrapped up), or null before the first one. With
    /// <paramref name="forUpdate"/>, locks its row.
    /// </summary>
    public static Task<HostedGame?> LoadRunningGameAsync(this DbSession db, string mode, bool forUpdate = false) =>
        db.SingleAsync(
            "SELECT id, mode, number, starts_at, ends_at, rules::text FROM games WHERE mode = $1 AND ended_at IS NULL"
            + " ORDER BY id DESC LIMIT 1" + (forUpdate ? " FOR UPDATE" : ""),
            ReadGame,
            mode);

    /// <summary>A game by id, running or ended.</summary>
    public static Task<HostedGame?> LoadGameAsync(this DbSession db, long id) =>
        db.SingleAsync("SELECT id, mode, number, starts_at, ends_at, rules::text FROM games WHERE id = $1", ReadGame, id);

    /// <summary>
    /// Starts a game of the given mode, numbered after the mode's last one, and returns it as stored. Call it holding
    /// the mode's lock (<see cref="LockGamesOfModeAsync"/>).
    /// </summary>
    public static async Task<HostedGame> InsertGameAsync(this DbSession db, NewGame start, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(start.Length, TimeSpan.Zero);
        var number = await db.ScalarAsync<int>("SELECT coalesce(max(number), 0) + 1 FROM games WHERE mode = $1", start.Mode);
        var round = new Round(number, now, now + start.Length);
        var id = await db.ScalarAsync<long>(
            """
            INSERT INTO games (mode, number, starts_at, ends_at, rules, created_at) VALUES ($1, $2, $3, $4, $5, $3)
            RETURNING id
            """,
            start.Mode, round.Number, round.StartsAt, round.EndsAt, new Jsonb(start.RulesJson));
        return (await db.LoadGameAsync(id))!; // as stored: PostgreSQL normalises the rules' JSON
    }

    /// <summary>Marks a game as over and wrapped up.</summary>
    public static Task EndGameAsync(this DbSession db, long id, DateTimeOffset now) =>
        db.ExecuteAsync("UPDATE games SET ended_at = $2 WHERE id = $1", id, now);

    /// <summary>Holds a lock on starting and ending games of a mode until the transaction ends.</summary>
    public static Task LockGamesOfModeAsync(this DbSession db, string mode) =>
        db.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext('games:' || $1))", mode);

    public static async Task<BanList> LoadBansAsync(this DbSession db) =>
        new((await db.QueryAsync(
                "SELECT owner_id, at, reason, banned_by FROM bans",
                r => new Ban(r.GetString(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.GetString(3))))
            .ToDictionary(b => b.OwnerId));

    public static async Task<bool> IsBannedAsync(this DbSession db, string ownerId) =>
        await db.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM bans WHERE owner_id = $1)", ownerId);

    private static HostedGame ReadGame(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetString(1),
            new Round(r.GetInt32(2), r.GetFieldValue<DateTimeOffset>(3), r.GetFieldValue<DateTimeOffset>(4)), r.GetString(5));
}
