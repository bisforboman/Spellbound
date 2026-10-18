using System.Reflection;
using System.Text.RegularExpressions;
using Npgsql;

namespace Spellbound.Web.Hosting;

/// <summary>A numbered SQL script, e.g. "002_add_index.sql" is version 2.</summary>
public sealed record SqlScript(int Version, string Name, string Sql);

/// <summary>A set of tables with its own numbered scripts: the host's, or a game's (see <see cref="IGameSchema"/>).</summary>
public sealed record SchemaModule(string Name, IReadOnlyList<SqlScript> Scripts)
{
    /// <summary>
    /// The scripts embedded in an assembly whose resource names start with <paramref name="resourcePrefix"/> and end
    /// in "NNN_name.sql". Add them to the project as &lt;EmbeddedResource Include="…/Sql/*.sql" /&gt;.
    /// </summary>
    public static SchemaModule FromResources(string name, Assembly assembly, string resourcePrefix) =>
        new(name,
        [
            .. assembly.GetManifestResourceNames()
                .Where(r => r.StartsWith(resourcePrefix, StringComparison.Ordinal) && r.EndsWith(".sql", StringComparison.Ordinal))
                .Select(r => (Resource: r, Match: Regex.Match(r, @"(\d+)_([\w-]+)\.sql$")))
                .Where(r => r.Match.Success)
                .Select(r => new SqlScript(int.Parse(r.Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), r.Match.Groups[2].Value, Read(assembly, r.Resource)))
                .OrderBy(s => s.Version),
        ]);

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Brings the database up to date at startup: runs each module's scripts that haven't run yet, in order, each in its
/// own transaction, and records them in <c>schema_versions</c>. An advisory lock keeps two app instances from running
/// them at once. Scripts are never edited once released; a change is a new, higher-numbered script.
/// </summary>
public static class SchemaMigrator
{
    private const long LockKey = 7_349_001; // any constant; the same for every instance of this app

    public static async Task<IReadOnlyList<string>> MigrateAsync(
        string connectionString, IEnumerable<SchemaModule> modules, CancellationToken cancellationToken = default)
    {
        var applied = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await Execute(connection, null, $"SELECT pg_advisory_lock({LockKey})", cancellationToken);
        try
        {
            await Execute(connection, null, """
                CREATE TABLE IF NOT EXISTS schema_versions (
                    module text NOT NULL,
                    version int NOT NULL,
                    name text NOT NULL,
                    applied_at timestamptz NOT NULL DEFAULT now(),
                    PRIMARY KEY (module, version))
                """, cancellationToken);
            foreach (var module in modules)
            {
                await using var read = new NpgsqlCommand("SELECT coalesce(max(version), 0) FROM schema_versions WHERE module = $1", connection);
                read.Parameters.Add(new NpgsqlParameter { Value = module.Name });
                var current = (int)(await read.ExecuteScalarAsync(cancellationToken))!;
                foreach (var script in module.Scripts.Where(s => s.Version > current))
                {
                    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                    await Execute(connection, transaction, script.Sql, cancellationToken);
                    await using var record = new NpgsqlCommand(
                        "INSERT INTO schema_versions (module, version, name) VALUES ($1, $2, $3)", connection, transaction);
                    record.Parameters.Add(new NpgsqlParameter { Value = module.Name });
                    record.Parameters.Add(new NpgsqlParameter { Value = script.Version });
                    record.Parameters.Add(new NpgsqlParameter { Value = script.Name });
                    await record.ExecuteNonQueryAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    applied.Add($"{module.Name} {script.Version:000}_{script.Name}");
                }
            }
        }
        finally
        {
            await Execute(connection, null, $"SELECT pg_advisory_unlock({LockKey})", CancellationToken.None);
        }

        return applied;
    }

    /// <summary>Whether no migration has ever run here: the only state a backup may be restored into.</summary>
    public static async Task<bool> IsEmptyAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('schema_versions') IS NULL", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
