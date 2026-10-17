using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Client.Documents;
using Raven.Embedded;
using Raven.Identity;
using Spellbound.Web.Components.Account;

namespace Spellbound.Web.Hosting;

/// <summary>
/// Wires up the reusable game host: embedded RavenDB, accounts, rounds, admin, backups and abuse protection.
/// The game adds its own services and implements <see cref="IRoundLifecycle"/> and <see cref="IPlayerSummaries"/>.
/// </summary>
public static class GameHostSetup
{
    public static WebApplicationBuilder AddGameHost(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.TryAddSingleton(TimeProvider.System);

        // Accounts: ASP.NET Core Identity with the template's Blazor account pages, stored in RavenDB.
        services.AddCascadingAuthenticationState();
        services.AddScoped<IdentityRedirectManager>();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        // Embedded RavenDB, started on first use so tests can swap in their own store.
        services.AddSingleton<IDocumentStore>(_ =>
        {
            var settings = configuration.GetSection("RavenDb");
            EmbeddedServer.Instance.StartServer(new ServerOptions
            {
                DataDirectory = Path.Combine(builder.Environment.ContentRootPath, settings["DataDirectory"] ?? "RavenData"),
                Licensing = HostStore.Licensing(
                    settings["License"] ?? configuration["RAVEN_License"],
                    required: !builder.Environment.IsDevelopment()),
            });
            return EmbeddedServer.Instance.GetDocumentStore(new DatabaseOptions(settings["Database"] ?? "Game")
            {
                Conventions = HostStore.Conventions(),
            });
        });
        services.AddScoped(sp => sp.GetRequiredService<IDocumentStore>().OpenAsyncSession()); // used by RavenDB.Identity

        services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
            .AddRavenDbIdentityStores<ApplicationUser>(_ => { })
            .AddUserManager<SavingUserManager>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

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

    /// <summary>The host's middleware, first in the pipeline; also starts the database and runs a pending restore.</summary>
    public static async Task UseGameHostAsync(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
        {
            app.UseForwardedHeaders();
        }

        app.UseRateLimiter(); // after forwarded headers, so limits are per player IP, not per proxy

        _ = app.Services.GetRequiredService<IDocumentStore>(); // start embedded RavenDB now, not on the first request

        if (app.Configuration["Backup:RestoreFrom"] is { Length: > 0 } restoreFrom)
        {
            await app.Services.GetRequiredService<BackupKeeper>().RestoreIfEmptyAsync(restoreFrom); // see docs/deploy.md
        }
    }

    /// <summary>The host's endpoints: health, and the extra endpoints the Identity account pages need.</summary>
    public static void MapGameHost(this WebApplication app)
    {
        app.MapHealthChecks("/healthz");
        app.MapAdditionalIdentityEndpoints();
    }
}
