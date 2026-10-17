using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Spellbound.Web.Hosting;

/// <summary>The hosting side of the database: connection, startup migration, and telling conflicts apart.</summary>
public static class HostStore
{
    /// <summary>The connection string's name (ConnectionStrings:Spellbound, or ConnectionStrings__Spellbound).</summary>
    public const string ConnectionStringName = "Spellbound";

    public static string ConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName) is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException(
                $"No database is configured. Set ConnectionStrings__{ConnectionStringName} to a PostgreSQL connection string.");

    /// <summary>A save that broke a unique key, such as a second mage for the same player in a round.</summary>
    public static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Whether the database has never been set up (no migration has run): the only state a restore may go into.</summary>
    public static async Task<bool> IsEmptyAsync(HostDb db, CancellationToken cancellationToken = default) =>
        !(await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any();
}
