using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Spellbound.Web.Hosting;

/// <summary>
/// Wires up the reusable game host: the PostgreSQL database, accounts, rounds, admin, backups and abuse protection.
/// The game adds its own services and tables (<see cref="IGameModel"/>) and implements the hooks in GameHooks.cs.
/// </summary>
public static class GameHostSetup
{
    public static WebApplicationBuilder AddGameHost(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.TryAddSingleton(TimeProvider.System);

        // Accounts: sign-in with Google (no passwords stored); see AccountEndpoints.cs and Accounts.cs.
        builder.AddAccounts();

        // PostgreSQL. Singletons (a Blazor circuit outlives one action) create a context per call from the factory.
        // The connection string is read when first used, so tests can set their own.
        services.AddDbContextFactory<HostDb>((sp, options) => options
            .UseNpgsql(HostStore.ConnectionString(sp.GetRequiredService<IConfiguration>()))
            .AddInterceptors(sp.GetServices<IInterceptor>()));

        // Rounds, admin, backups, multi-account signals.
        services.AddSingleton<RoundKeeper>();
        services.AddHostedService(sp => sp.GetRequiredService<RoundKeeper>());
        services.AddSingleton<BackupKeeper>();
        services.AddHostedService(sp => sp.GetRequiredService<BackupKeeper>());
        services.AddSingleton<AccountSignals>();
        services.AddSingleton<AdminService>();
        services.AddOptions<AdminPageOptions>();
        services.AddSingleton<IAuthorizationHandler, AdminHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy.Name, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
        services.AddHealthChecks();

        var rateLimits = configuration.GetSection("RateLimits").Get<RateLimits>() ?? new RateLimits();
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = rateLimits.CreateLimiter();
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, token) =>
                new ValueTask(context.HttpContext.Response.WriteAsync(RateLimits.RejectionMessage, token));
        });

        // Behind Caddy (docs/deploy.md), trust its X-Forwarded-* headers so the app sees HTTPS and real client IPs.
        // Only switch this on when the app isn't reachable except through the proxy, as in docker-compose.yml.
        if (configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        // Keep the keys that protect sign-in cookies on the data volume, so a restart doesn't sign everyone out.
        if (configuration["DataProtection:KeysDirectory"] is { Length: > 0 } keysDirectory)
        {
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
        }

        return builder;
    }

    /// <summary>
    /// The host's middleware, first in the pipeline. Also brings the database up to date: a pending restore goes into an
    /// empty database first (docs/deploy.md), then any new migrations run.
    /// </summary>
    public static async Task UseGameHostAsync(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
        {
            app.UseForwardedHeaders();
        }

        app.UseRateLimiter(); // after forwarded headers, so limits are per player IP, not per proxy

        if (app.Configuration["Backup:RestoreFrom"] is { Length: > 0 } restoreFrom)
        {
            await app.Services.GetRequiredService<BackupKeeper>().RestoreIfEmptyAsync(restoreFrom);
        }

        await using var db = await app.Services.GetRequiredService<IDbContextFactory<HostDb>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>The host's endpoints: health, and sign-in, sign-out and account deletion.</summary>
    public static void MapGameHost(this WebApplication app)
    {
        app.MapHealthChecks("/healthz");
        app.MapAccounts();
    }
}
