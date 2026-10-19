using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>
/// Base for tests that need the database: each test gets a fresh PostgreSQL database, copied from a template that is
/// migrated once per test run, which is much faster than migrating each one. The server comes from the
/// SPELLBOUND_TEST_POSTGRES connection string (default: postgres/postgres on localhost, as in CI).
/// </summary>
public abstract class TestDatabase : IDisposable
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("SPELLBOUND_TEST_POSTGRES") is { Length: > 0 } server
            ? server
            : "Host=localhost;Username=postgres;Password=postgres";

    private static readonly Lazy<string> Template = new(CreateTemplate);

    /// <summary>The migrated template every test database is copied from.</summary>
    protected static string MigratedTemplate => Template.Value;

    private readonly List<string> databases = [];

    protected TestDatabase()
    {
        ConnectionString = CreateDatabase(Template.Value);
    }

    protected string ConnectionString { get; }

    /// <summary>The real app on this test's database.</summary>
    protected WebApplicationFactory<Program> CreateApp(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"ConnectionStrings:{HostStore.ConnectionStringName}", ConnectionString);
            configure?.Invoke(builder);
        });

    /// <summary>Runs something in one committed transaction on this test's database (or another one), outside the app.</summary>
    protected async Task<T> WithDbAsync<T>(Func<DbSession, Task<T>> work, string? connectionString = null)
    {
        await using var source = NpgsqlDataSource.Create(connectionString ?? ConnectionString);
        await using var db = await new HostDatabase(source).BeginAsync();
        var result = await work(db);
        await db.CommitAsync();
        return result;
    }

    /// <summary>The number from a counting query on this test's database, e.g. "SELECT count(*) FROM accounts".</summary>
    protected Task<long> CountAsync(string sql, params object?[] args) =>
        WithDbAsync(db => db.ScalarAsync<long>(sql, args));

    /// <summary>Another database for this test, dropped with it: a copy of the migrated template, or empty.</summary>
    protected string CreateDatabase(string? template = null)
    {
        var name = "spellbound_test_" + Guid.NewGuid().ToString("N");
        Execute(template is null ? $"CREATE DATABASE {name}" : $"CREATE DATABASE {name} TEMPLATE {template}");
        databases.Add(name);
        return WithDatabase(name);
    }

    public virtual void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        foreach (var name in databases)
        {
            Execute($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
        }

        GC.SuppressFinalize(this);
    }

    private static string CreateTemplate()
    {
        var template = $"spellbound_test_template_{Environment.ProcessId}";
        Execute($"DROP DATABASE IF EXISTS {template} WITH (FORCE)");
        Execute($"CREATE DATABASE {template}");
        // No pooling, so no connection is left open: PostgreSQL only copies a template nobody is connected to.
        SchemaMigrator.MigrateAsync(WithDatabase(template) + ";Pooling=false", [HostStore.Schema, SpellboundGame.GameSchema])
            .GetAwaiter().GetResult();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Execute($"DROP DATABASE IF EXISTS {template} WITH (FORCE)");
        return template;
    }

    private static string WithDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(Server) { Database = database }.ConnectionString;

    private static void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(Server + ";Pooling=false");
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
