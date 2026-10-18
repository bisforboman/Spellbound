using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Spellbound.Web.Hosting;

/// <summary>
/// Sign-in, sign-out and account deletion. Players sign in with Google; Google's answer lands in a short-lived
/// external cookie, and <c>/Account/SignedIn</c> turns it into an account and the real sign-in cookie. In Development
/// only, <c>/Account/DevSignIn</c> signs in with just an email, for local runs and tests without a Google client.
/// </summary>
public static class AccountEndpoints
{
    public const string ExternalScheme = "External";

    public const string DevProvider = "dev";

    public const string GoogleProvider = "google";

    /// <summary>A sign-in cookie is checked against the database (deleted? banned?) at most this often.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private const string CheckedAt = "checked-at";

    /// <summary>Whether Google sign-in is configured (Authentication:Google:ClientId and ClientSecret).</summary>
    public static bool GoogleConfigured(IConfiguration configuration) =>
        configuration["Authentication:Google:ClientId"] is { Length: > 0 };

    public static void AddAccounts(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var google = builder.Configuration.GetSection("Authentication:Google");
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, AccountRevalidation>();
        services.AddSingleton<AccountService>();

        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = ValidateAsync;
            })
            .AddCookie(ExternalScheme, options => options.ExpireTimeSpan = TimeSpan.FromMinutes(10));

        if (GoogleConfigured(builder.Configuration))
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"] is { Length: > 0 } secret
                    ? secret
                    : throw new InvalidOperationException("Set Authentication__Google__ClientSecret as well as the client id.");
                options.SignInScheme = ExternalScheme;
            });
        }
        else if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Sign-in isn't configured. Set Authentication__Google__ClientId and Authentication__Google__ClientSecret "
                + "(see docs/deploy.md).");
        }
    }

    public static void MapAccounts(this WebApplication app)
    {
        var account = app.MapGroup("/Account");

        account.MapPost("/ExternalLogin", IResult ([FromForm] string? returnUrl) => GoogleConfigured(app.Configuration)
            ? TypedResults.Challenge(
                new AuthenticationProperties { RedirectUri = $"/Account/SignedIn?returnUrl={Uri.EscapeDataString(LocalUrl(returnUrl))}" },
                [GoogleDefaults.AuthenticationScheme])
            : Refuse("Google sign-in isn't set up on this server."));

        account.MapGet("/SignedIn", async (HttpContext context, [FromQuery] string? returnUrl) =>
        {
            var external = await context.AuthenticateAsync(ExternalScheme);
            await context.SignOutAsync(ExternalScheme);
            return external.Principal is { } google
                ? await CompleteSignInAsync(
                    context,
                    GoogleProvider,
                    google.FindFirstValue(ClaimTypes.NameIdentifier),
                    google.FindFirstValue(ClaimTypes.Email),
                    returnUrl)
                : Refuse("The sign-in with Google didn't finish. Please try again.");
        });

        if (app.Environment.IsDevelopment())
        {
            account.MapPost("/DevSignIn", (HttpContext context, [FromForm] string email, [FromForm] string? returnUrl) =>
                CompleteSignInAsync(context, DevProvider, email.Trim().ToLowerInvariant(), email, returnUrl));
        }

        account.MapPost("/Logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return TypedResults.LocalRedirect("~/");
        });

        account.MapGet("/Data", async (ClaimsPrincipal user, AccountService accounts) => TypedResults.File(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                await accounts.ExportAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }),
            "application/json",
            "spellbound-data.json")).RequireAuthorization();

        // Notifications: the browser subscribes with the server's key (push.js), and the player can send a test.
        account.MapGet("/Push/Key", async (PushStore push) => TypedResults.Text(await push.PublicKeyAsync())).RequireAuthorization();
        account.MapPost("/Push", async (ClaimsPrincipal user, [FromForm] string endpoint, [FromForm] string p256dh, [FromForm] string auth, PushStore push) =>
            await push.SubscribeAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, endpoint, p256dh, auth) is { } problem
                ? Results.BadRequest(problem)
                : Results.NoContent()).RequireAuthorization();
        account.MapPost("/Push/Remove", async (ClaimsPrincipal user, [FromForm] string endpoint, PushStore push) =>
        {
            await push.UnsubscribeAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, endpoint);
            return Results.NoContent();
        }).RequireAuthorization();
        account.MapPost("/Push/Test", (ClaimsPrincipal user, PushSink push) =>
        {
            push.Send(new PushMessage(
                user.FindFirstValue(ClaimTypes.NameIdentifier)!, "Spellbound", "Notifications work on this device.", "/Account/Manage"));
            return Results.LocalRedirect("~/Account/Manage?push=sent");
        }).RequireAuthorization();

        account.MapPost("/Delete", async (HttpContext context, ClaimsPrincipal user, [FromForm] string? confirm, AccountService accounts) =>
        {
            if (confirm != "delete")
            {
                return Results.LocalRedirect("~/Account/Manage?error=confirm");
            }

            await accounts.DeleteAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("~/Account/Login?deleted=1");
        }).RequireAuthorization();
    }

    private static async Task<IResult> CompleteSignInAsync(
        HttpContext context, string provider, string? subject, string? email, string? returnUrl)
    {
        var services = context.RequestServices;
        var result = await services.GetRequiredService<AccountService>().SignInAsync(provider, subject ?? "", email ?? "");
        if (result.Account is not { } account)
        {
            return Refuse(result.Refused!);
        }

        await services.GetRequiredService<AccountSignals>().RecordAsync(account.Id, context.Connection.RemoteIpAddress);
        var properties = new AuthenticationProperties { IsPersistent = true };
        properties.Items[CheckedAt] = Stamp(services.GetRequiredService<TimeProvider>().GetUtcNow()); // just checked
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AccountService.Principal(account, CookieAuthenticationDefaults.AuthenticationScheme),
            properties);
        return TypedResults.LocalRedirect(LocalUrl(returnUrl));
    }

    private static RedirectHttpResult Refuse(string message) =>
        TypedResults.LocalRedirect($"~/Account/Login?error={Uri.EscapeDataString(message)}");

    /// <summary>Only redirects within the site, so a crafted link can't send a player elsewhere after signing in.</summary>
    private static string LocalUrl(string? url) =>
        url is { Length: > 0 } && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";

    /// <summary>Signs out a cookie whose account was deleted or banned. Checks the database at most once a minute.</summary>
    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        if (context.Properties.Items.TryGetValue(CheckedAt, out var checkedAt)
            && DateTimeOffset.TryParse(checkedAt, System.Globalization.CultureInfo.InvariantCulture, out var at)
            && now - at < CheckEvery)
        {
            return;
        }

        if (!await context.HttpContext.RequestServices.GetRequiredService<AccountService>().IsActiveAsync(context.Principal!))
        {
            // Rejecting only ignores the cookie for this request; signing out also deletes it (Microsoft's pattern).
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        // Rewrite the cookie with the new check time, so the next minute's requests skip the database.
        context.Properties.Items[CheckedAt] = Stamp(now);
        context.ReplacePrincipal(context.Principal!);
        context.ShouldRenew = true;
    }

    private static string Stamp(DateTimeOffset at) => at.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Signs an open game connection out within a few minutes of its account being deleted or banned.</summary>
internal sealed class AccountRevalidation(ILoggerFactory loggerFactory, AccountService accounts)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken) =>
        accounts.IsActiveAsync(state.User);
}
