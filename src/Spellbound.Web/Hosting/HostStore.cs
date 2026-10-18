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
