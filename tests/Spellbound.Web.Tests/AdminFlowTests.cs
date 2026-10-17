using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Client.Documents;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>The admin policy, bans and round changes through the real app, with real accounts.</summary>
public sealed class AdminFlowTests : SpellboundTestDriver, IAsyncLifetime
{
    private const string AdminEmail = "admin@spellbound.test";

    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, StartingGold = 200_000 };

    private readonly IDocumentStore store;
    private readonly WebApplicationFactory<Program> factory;
    private string adminId = string.Empty;
    private string merlinId = string.Empty;
    private string morganaId = string.Empty;

    public AdminFlowTests()
    {
        store = GetDocumentStore();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Admins:0", AdminEmail)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IDocumentStore>();
                services.AddSingleton(store);
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
    public async Task Admin_can_extend_the_round_or_end_it_now()
    {
        var round = (await Game.GetRoundAsync())!;

        Assert.Equal($"Round 1 now ends {round.EndsAt.AddDays(7):u}.", await Admin.ChangeRoundEndAsync(round.EndsAt.AddDays(7)));
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

    private async Task<bool> IsLockedOutAsync(string ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.IsLockedOutAsync((await users.FindByIdAsync(ownerId))!);
    }
}
