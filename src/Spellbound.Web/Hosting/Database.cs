using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Spellbound.Web.Hosting;

/// <summary>A jsonb parameter: JSON text that PostgreSQL stores as jsonb.</summary>
public sealed record Jsonb(string Json);

/// <summary>
/// The database: PostgreSQL through plain Npgsql. Every call opens a <see cref="DbSession"/>, one transaction on one
/// pooled connection, so a Blazor circuit (which outlives one action) never holds a connection between actions.
/// </summary>
public sealed class HostDatabase(NpgsqlDataSource dataSource)
{
    /// <summary>How long a statement waits for a row another action has locked before giving up ("busy").</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    public async Task<DbSession> BeginAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var session = new DbSession(connection, transaction);
            await session.ExecuteAsync($"SET LOCAL lock_timeout = '{(int)LockTimeout.TotalMilliseconds}ms'");
            return session;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// One transaction. Statements use positional parameters ($1, $2, …) given in order; a <see cref="DateTimeOffset"/> is
/// stored as UTC (timestamptz only takes UTC) and a <see cref="Jsonb"/> as jsonb. Nothing is saved unless
/// <see cref="CommitAsync"/> is called.
/// </summary>
public sealed class DbSession(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    public Task<int> ExecuteAsync(string sql, params object?[] args) => Command(sql, args).ExecuteNonQueryAsync();

    /// <summary>The first column of the first row, or default when there is no row (or it is null).</summary>
    public async Task<T?> ScalarAsync<T>(string sql, params object?[] args)
    {
        await using var reader = await Command(sql, args).ExecuteReaderAsync();
        return await reader.ReadAsync() && !await reader.IsDBNullAsync(0) ? reader.GetFieldValue<T>(0) : default;
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, params object?[] args)
    {
        await using var reader = await Command(sql, args).ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public async Task<T?> SingleAsync<T>(string sql, Func<NpgsqlDataReader, T> map, params object?[] args)
        where T : class =>
        (await QueryAsync(sql, map, args)).SingleOrDefault();

    public Task CommitAsync() => transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }

    private NpgsqlCommand Command(string sql, object?[] args)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var arg in args)
        {
            command.Parameters.Add(arg switch
            {
                null => new NpgsqlParameter { Value = DBNull.Value },
                Jsonb json => new NpgsqlParameter { Value = json.Json, NpgsqlDbType = NpgsqlDbType.Jsonb },
                DateTimeOffset at => new NpgsqlParameter { Value = at.ToUniversalTime() },
                _ => new NpgsqlParameter { Value = arg },
            });
        }

        return command;
    }
}
