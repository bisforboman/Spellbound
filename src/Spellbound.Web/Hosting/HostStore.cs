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

    /// <summary>The round being played, or null before the first one. With <paramref name="forUpdate"/>, locks it.</summary>
    public static Task<Round?> LoadCurrentRoundAsync(this DbSession db, bool forUpdate = false) =>
        db.SingleAsync(
            "SELECT number, starts_at, ends_at FROM current_round WHERE id = 1" + (forUpdate ? " FOR UPDATE" : ""),
            r => new Round(r.GetInt32(0), r.GetFieldValue<DateTimeOffset>(1), r.GetFieldValue<DateTimeOffset>(2)));

    public static async Task<BanList> LoadBansAsync(this DbSession db) =>
        new((await db.QueryAsync(
                "SELECT owner_id, at, reason, banned_by FROM bans",
                r => new Ban(r.GetString(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.GetString(3))))
            .ToDictionary(b => b.OwnerId));

    public static async Task<bool> IsBannedAsync(this DbSession db, string ownerId) =>
        await db.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM bans WHERE owner_id = $1)", ownerId);
}
