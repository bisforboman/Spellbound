using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>The admin policy, bans and round changes through the real app, with real accounts.</summary>
public sealed class AdminFlowTests : TestDatabase, IAsyncLifetime
{
    private const string AdminEmail = "admin@spellbound.test";

    private readonly WebApplicationFactory<Program> factory;
    private string adminId = string.Empty;
    private string merlinId = string.Empty;
    private string morganaId = string.Empty;

    public AdminFlowTests()
    {
        factory = CreateApp(b => b.UseSetting("Admins:0", AdminEmail));
    }

    private PlaceholderGame Game => factory.Services.GetRequiredService<PlaceholderGame>();

    private AdminService Admin => factory.Services.GetRequiredService<AdminService>();

    private AccountService Accounts => factory.Services.GetRequiredService<AccountService>();

    public async Task InitializeAsync()
    {
        await factory.Services.GetRequiredService<RoundKeeper>().TickAsync();
        adminId = await CreateUserAsync(AdminEmail);
        merlinId = await CreateUserAsync("merlin@spellbound.test");
        morganaId = await CreateUserAsync("morgana@spellbound.test");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_listed_emails_are_admins()
    {
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(User(AdminEmail), AdminPolicy.Name)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(User("ADMIN@spellbound.test"), AdminPolicy.Name)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(User("merlin@spellbound.test"), AdminPolicy.Name)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), AdminPolicy.Name)).Succeeded);
    }

    [Fact]
    public async Task Banned_player_is_locked_out_until_unbanned()
    {
        Assert.Equal("Banned morgana@spellbound.test.", await Admin.BanAsync(adminId, AdminEmail, morganaId, "Multi-accounting"));

        Assert.True(await IsShutOutAsync(morganaId));
        Assert.Equal(AccountService.Banned, (await Accounts.SignInAsync(AccountEndpoints.DevProvider, "morgana@spellbound.test", "morgana@spellbound.test")).Refused);
        var player = Assert.Single(await Admin.GetPlayersAsync(), p => p.OwnerId == morganaId);
        Assert.Equal("Multi-accounting", player.Ban!.Reason);

        Assert.Equal("Unbanned morgana@spellbound.test.", await Admin.UnbanAsync(morganaId));

        Assert.False(await IsShutOutAsync(morganaId));
    }

    [Fact]
    public async Task Ban_needs_a_reason_and_admins_cannot_ban_themselves()
    {
        Assert.Equal("You can't ban yourself.", await Admin.BanAsync(adminId, AdminEmail, adminId, "Oops"));
        Assert.StartsWith("Give a reason", await Admin.BanAsync(adminId, AdminEmail, merlinId, "  "), StringComparison.Ordinal);
        Assert.All(await Admin.GetPlayersAsync(), p => Assert.Null(p.Ban));
        Assert.False(await IsShutOutAsync(merlinId));
    }

    [Fact]
    public async Task A_banned_player_who_deletes_their_account_stays_banned()
    {
        await Admin.BanAsync(adminId, AdminEmail, morganaId, "Cheating");
        await Accounts.DeleteAsync(morganaId);

        var again = await Accounts.SignInAsync(AccountEndpoints.DevProvider, "morgana@spellbound.test", "morgana@spellbound.test");

        Assert.Equal(AccountService.Banned, again.Refused);
    }

    [Fact]
    public async Task Admin_can_extend_the_round_or_end_it_now()
    {
        var round = (await Game.GetRoundAsync())!;

        Assert.Equal($"Round 1 now ends {round.EndsAt.AddDays(7):u}.", await Admin.ChangeRoundEndAsync(round.EndsAt.AddDays(7)));
        var local = round.EndsAt.AddDays(8).ToOffset(TimeSpan.FromHours(2)); // a time typed in another time zone
        await Admin.ChangeRoundEndAsync(local);
        Assert.Equal(local, (await Game.GetRoundAsync())!.EndsAt);
        Assert.StartsWith("Round 1 started", await Admin.ChangeRoundEndAsync(round.StartsAt), StringComparison.Ordinal);
        Assert.Equal(1, (await Game.GetRoundAsync())!.Number);

        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        Assert.Equal("Round 1 has ended and round 2 has started.", await Admin.ChangeRoundEndAsync(now));

        Assert.Equal(2, (await Game.GetRoundAsync())!.Number);
    }

    [Fact]
    public async Task The_admin_page_is_for_admins_only()
    {
        using var admin = Client();
        using var player = Client();
        await SignInAsync(admin, AdminEmail);
        await SignInAsync(player, "merlin@spellbound.test");

        var page = await admin.GetStringAsync(new Uri("/admin", UriKind.Relative));
        var refused = await player.GetAsync(new Uri("/admin", UriKind.Relative));

        Assert.Contains("Problems", page, StringComparison.Ordinal);
        Assert.Contains("Account/AccessDenied", refused.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_home_page_shows_the_round_to_a_signed_in_player()
    {
        using var merlin = Client();
        await SignInAsync(merlin, "merlin@spellbound.test");

        var home = await merlin.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("The game is being designed again", home, StringComparison.Ordinal);
        Assert.Contains("Round 1 runs until", home, StringComparison.Ordinal);
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private static ClaimsPrincipal User(string email) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, email)], "Test"));

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Signs in through the Development sign-in form, so the client carries the sign-in cookie.</summary>
    private static async Task SignInAsync(HttpClient client, string email)
    {
        var login = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        var token = System.Text.RegularExpressions.Regex.Match(
            login, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<v>[^\"]+)\"").Groups["v"].Value;
        var response = await client.PostAsync(
            new Uri("/Account/DevSignIn", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(token),
                ["email"] = email,
                ["returnUrl"] = "/",
            }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<string> CreateUserAsync(string email) =>
        (await Accounts.SignInAsync(AccountEndpoints.DevProvider, email, email)).Account!.Id;

    /// <summary>Whether the account's sign-in cookie would be refused (it's deleted or banned).</summary>
    private async Task<bool> IsShutOutAsync(string ownerId) =>
        !await Accounts.IsActiveAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, ownerId)], "Test")));
}
