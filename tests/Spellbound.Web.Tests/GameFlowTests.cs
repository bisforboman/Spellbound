using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Spellbound.Domain;
using Spellbound.Web;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>Runs the real app against a fresh in-memory RavenDB database per test.</summary>
public sealed class GameFlowTests : SpellboundTestDriver, IAsyncLifetime
{
    private const string Owner = "ApplicationUsers/1-A";

    private readonly IDocumentStore store;
    private readonly WebApplicationFactory<Program> factory;

    public GameFlowTests()
    {
        store = GetDocumentStore();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDocumentStore>();
            services.AddSingleton(store);
        }));
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

    public async Task InitializeAsync() => await factory.Services.GetRequiredService<RoundKeeper>().TickAsync(); // round 1

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Action_spends_turns_saves_the_mage_and_writes_news()
    {
        Assert.True((await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant)).Succeeded);

        var report = await Game.ExploreAsync(Owner, 5);

        Assert.True(report.Succeeded);
        var mage = await Game.GetMageAsync(Owner);
        Assert.NotNull(mage);
        Assert.Equal(95, mage.Turns);
        Assert.True(mage.Land > 250);
        var news = Assert.Single(await Game.GetNewsAsync(Owner));
        Assert.StartsWith("You explored", news.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_action_changes_nothing()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant);

        var report = await Game.ExploreAsync(Owner, 1_000);

        Assert.False(report.Succeeded);
        var mage = await Game.GetMageAsync(Owner);
        Assert.NotNull(mage);
        Assert.Equal(100, mage.Turns);
        Assert.Equal(250, mage.Land);
        Assert.Empty(await Game.GetNewsAsync(Owner));
    }

    [Fact]
    public async Task Cast_persists_the_active_effect()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant);

        Assert.True((await Game.CastAsync(Owner, "overgrowth")).Succeeded);

        var mage = await Game.GetMageAsync(Owner);
        var effect = Assert.Single(mage!.ActiveEffects);
        Assert.Equal("overgrowth", effect.SpellId);
    }

    [Fact]
    public async Task Second_mage_for_the_same_player_is_refused()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant);

        Assert.False((await Game.CreateMageAsync(Owner, "Morgana", MagicSchool.Umbral)).Succeeded);
        Assert.Equal("Merlin", (await Game.GetMageAsync(Owner))!.Name);
    }

    [Fact]
    public async Task News_log_keeps_the_last_50_reports()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant);

        for (var i = 0; i < GameService.NewsKept + 3; i++)
        {
            await Game.MeditateAsync(Owner, 1);
        }

        Assert.Equal(GameService.NewsKept, (await Game.GetNewsAsync(Owner)).Count);
    }

    [Fact]
    public async Task Concurrent_saves_of_the_same_mage_cannot_both_spend_turns()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant);
        var rules = factory.Services.GetRequiredService<GameRules>();

        // A second tap saves the mage after GameService loaded it but before GameService saves.
        var interfered = false;
        store.OnBeforeStore += (_, args) =>
        {
            if (interfered || args.DocumentId != RavenSetup.MageKey(1, Owner))
            {
                return;
            }

            interfered = true;
            using var other = store.OpenSession();
            other.Load<Mage>(RavenSetup.MageKey(1, Owner)).Meditate(1, DateTimeOffset.UtcNow, rules);
            other.SaveChanges();
        };

        var report = await Game.ExploreAsync(Owner, 10);

        Assert.True(interfered);
        Assert.False(report.Succeeded);
        var mage = await Game.GetMageAsync(Owner);
        Assert.Equal(99, mage!.Turns); // only the other tap's single turn was spent
        Assert.Equal(250, mage.Land);
    }

    [Fact]
    public async Task New_account_is_found_by_name_at_once_and_updates_are_saved()
    {
        const string email = "merlin@spellbound.test";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await users.CreateAsync(new ApplicationUser { UserName = email, Email = email }, "Test-Only-1!");
            Assert.True(created.Succeeded);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(email); // what password sign-in uses
            Assert.NotNull(user);
            await users.AccessFailedAsync(user);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Equal(1, (await users.FindByEmailAsync(email))!.AccessFailedCount);
        }
    }

    [Fact]
    public async Task Game_pages_require_sign_in_and_manifest_is_served()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var home = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Contains("Account/Login", home.Headers.Location!.ToString(), StringComparison.Ordinal);

        var manifest = await client.GetAsync(new Uri("/manifest.webmanifest", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }
}
