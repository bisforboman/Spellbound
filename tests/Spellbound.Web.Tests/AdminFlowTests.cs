using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>The admin policy, bans and round changes through the real app, with real accounts.</summary>
public sealed class AdminFlowTests : TestDatabase, IAsyncLifetime
{
    private const string AdminEmail = "admin@spellbound.test";

    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, ProtectionDuration = TimeSpan.Zero, StartingGold = 200_000 };

    private readonly WebApplicationFactory<Program> factory;
    private string adminId = string.Empty;
    private string merlinId = string.Empty;
    private string morganaId = string.Empty;

    public AdminFlowTests()
    {
        factory = CreateApp(b => b
            .UseSetting("Admins:0", AdminEmail)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<GameRules>();
                services.AddSingleton(Rules);
            }));
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

    private AdminService Admin => factory.Services.GetRequiredService<AdminService>();

    public async Task InitializeAsync()
    {
        await factory.Services.GetRequiredService<RoundKeeper>().TickAsync();
        adminId = await CreateUserAsync(AdminEmail);
        merlinId = await CreateUserAsync("merlin@spellbound.test");
        morganaId = await CreateUserAsync("morgana@spellbound.test");
        await Game.CreateMageAsync(merlinId, "Merlin", MagicSchool.Tidal);
        await Game.CreateMageAsync(morganaId, "Morgana", MagicSchool.Ember);
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
    public async Task Banned_player_is_locked_out_and_removed_from_play_until_unbanned()
    {
        var morgana = (await Game.GetMageAsync(morganaId))!.Id;

        Assert.Equal("Banned morgana@spellbound.test.", await Admin.BanAsync(adminId, AdminEmail, morganaId, "Multi-accounting"));

        Assert.True(await IsLockedOutAsync(morganaId));
        Assert.Equal("Your account is banned.", (await Game.ExploreAsync(morganaId, 1)).Lines[0]);
        Assert.DoesNotContain(await Game.GetAllMagesAsync(), m => m.OwnerId == morganaId);
        Assert.Null(await Game.GetRivalAsync(morgana));
        await Game.RecruitAsync(merlinId, "knights", 100);
        Assert.False((await Game.AttackAsync(merlinId, morgana)).Succeeded);
        var player = Assert.Single(await Admin.GetPlayersAsync(), p => p.OwnerId == morganaId);
        Assert.Equal("Multi-accounting", player.Ban!.Reason);

        Assert.Equal("Unbanned morgana@spellbound.test.", await Admin.UnbanAsync(morganaId));

        Assert.False(await IsLockedOutAsync(morganaId));
        Assert.True((await Game.ExploreAsync(morganaId, 1)).Succeeded);
        Assert.True((await Game.AttackAsync(merlinId, morgana)).Succeeded);
    }

    [Fact]
    public async Task Ban_needs_a_reason_and_admins_cannot_ban_themselves()
    {
        Assert.Equal("You can't ban yourself.", await Admin.BanAsync(adminId, AdminEmail, adminId, "Oops"));
        Assert.StartsWith("Give a reason", await Admin.BanAsync(adminId, AdminEmail, merlinId, "  "), StringComparison.Ordinal);
        Assert.All(await Admin.GetPlayersAsync(), p => Assert.Null(p.Ban));
        Assert.False(await IsLockedOutAsync(merlinId));
    }

    [Fact]
    public async Task Admin_password_reset_gives_a_working_temporary_password_and_lifts_a_lockout()
    {
        for (var i = 0; i < 5; i++)
        {
            await WithUsersAsync(async users => await users.AccessFailedAsync((await users.FindByIdAsync(merlinId))!));
        }

        Assert.True(await IsLockedOutAsync(merlinId)); // five wrong guesses

        var message = await Admin.ResetPasswordAsync(merlinId);

        var password = Regex.Match(message, @"[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}").Value;
        Assert.StartsWith("Temporary password for merlin@spellbound.test: ", message, StringComparison.Ordinal);
        Assert.False(await IsLockedOutAsync(merlinId));
        Assert.True(await CheckPasswordAsync(merlinId, password));
        Assert.False(await CheckPasswordAsync(merlinId, "Test-Only-1!"));
    }

    [Fact]
    public async Task Password_can_be_reset_by_email_for_an_account_without_a_mage()
    {
        var newcomer = await CreateUserAsync("newcomer@spellbound.test"); // no mage this round, so not in the player list
        Assert.DoesNotContain(await Admin.GetPlayersAsync(), p => p.OwnerId == newcomer);

        var message = await Admin.ResetPasswordByEmailAsync(" newcomer@spellbound.test ");

        Assert.False(await CheckPasswordAsync(newcomer, "Test-Only-1!"));
        Assert.True(await CheckPasswordAsync(newcomer, Regex.Match(message, @"\w{4}-\w{4}-\w{4}").Value));
        Assert.Equal("No account has that email.", await Admin.ResetPasswordByEmailAsync("nobody@spellbound.test"));
    }

    [Fact]
    public async Task Banned_players_password_is_not_reset()
    {
        await Admin.BanAsync(adminId, AdminEmail, morganaId, "Cheating");

        Assert.StartsWith("That player is banned", await Admin.ResetPasswordAsync(morganaId), StringComparison.Ordinal);
        Assert.True(await CheckPasswordAsync(morganaId, "Test-Only-1!"));
        Assert.True(await IsLockedOutAsync(morganaId));
    }

    [Fact]
    public void Temporary_passwords_are_readable_and_different_each_time()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => AdminService.TemporaryPassword()).ToList();

        Assert.All(passwords, p => Assert.Matches("^[A-HJ-NP-Z][a-km-z][2-9][a-km-z](-[A-HJ-NP-Z][a-km-z][2-9][a-km-z]){2}$", p));
        Assert.Equal(passwords.Count, passwords.Distinct().Count());
    }

    [Fact]
    public async Task Forgot_password_page_points_to_the_admin_while_no_email_is_sent()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var html = await client.GetStringAsync(new Uri("/Account/ForgotPassword", UriKind.Relative));

        Assert.Contains("Ask the game's admin to reset your password", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.Email", html, StringComparison.Ordinal); // no form that would promise an email
    }

    [Fact]
    public async Task Change_email_page_does_not_promise_an_email_while_none_is_sent()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await SignInAsync(client, "merlin@spellbound.test");

        var html = await client.GetStringAsync(new Uri("/Account/Manage/Email", UriKind.Relative));

        Assert.Contains("which this server can't send yet", html, StringComparison.Ordinal);
        Assert.Contains("merlin@spellbound.test", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.NewEmail", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_an_account_removes_the_players_mage_and_news()
    {
        await Game.ExploreAsync(merlinId, 1); // some news too
        var merlin = (await Game.GetMageAsync(merlinId))!.Id;

        await WithUsersAsync(async users => Assert.True((await users.DeleteAsync((await users.FindByIdAsync(merlinId))!)).Succeeded));

        Assert.Null(await Game.GetMageAsync(merlinId));
        Assert.Empty(await Game.GetNewsAsync(merlinId));
        Assert.Null(await Game.GetRivalAsync(merlin));
        Assert.Equal(["Morgana"], (await Game.GetAllMagesAsync()).Select(m => m.Name)); // nobody else's data
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

        await Admin.BanAsync(adminId, AdminEmail, morganaId, "Cheating");
        Assert.Equal("Round 1 has ended and round 2 has started.", await Admin.ChangeRoundEndAsync(Game.Now));

        Assert.Equal(2, (await Game.GetRoundAsync())!.Number);
        var fame = Assert.Single(await Game.GetHallOfFameAsync());
        Assert.Equal(["Merlin"], fame.ByLand.Select(e => e.Name)); // banned players are left out
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private static ClaimsPrincipal User(string email) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, email)], "Test"));

    private async Task<string> CreateUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user, "Test-Only-1!")).Succeeded);
        return user.Id!;
    }

    /// <summary>Logs in through the real login form, so the client carries the sign-in cookie.</summary>
    private static async Task SignInAsync(HttpClient client, string email)
    {
        var page = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        var fields = Regex.Matches(page, "<input[^>]*type=\"hidden\"[^>]*name=\"(?<n>[^\"]+)\"[^>]*value=\"(?<v>[^\"]*)\"")
            .ToDictionary(m => System.Net.WebUtility.HtmlDecode(m.Groups["n"].Value), m => System.Net.WebUtility.HtmlDecode(m.Groups["v"].Value));
        fields["Input.Email"] = email;
        fields["Input.Password"] = "Test-Only-1!";
        var response = await client.PostAsync(new Uri("/Account/Login", UriKind.Relative), new FormUrlEncodedContent(fields));
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Redirect, $"Login failed: {response.StatusCode}");
    }

    private async Task WithUsersAsync(Func<UserManager<ApplicationUser>, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    private async Task<bool> CheckPasswordAsync(string ownerId, string password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.CheckPasswordAsync((await users.FindByIdAsync(ownerId))!, password);
    }

    private async Task<bool> IsLockedOutAsync(string ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.IsLockedOutAsync((await users.FindByIdAsync(ownerId))!);
    }
}
