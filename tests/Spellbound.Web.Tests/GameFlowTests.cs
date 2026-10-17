using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spellbound.Domain;
using Spellbound.Web;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>Runs the real app against a private in-memory SQLite database (migrated at startup).</summary>
public sealed class GameFlowTests : IDisposable
{
    private const string Owner = "user-1";

    private readonly SqliteConnection keepAlive; // a shared in-memory database lives as long as one connection is open
    private readonly WebApplicationFactory<Program> factory;

    public GameFlowTests()
    {
        var connectionString = $"DataSource=file:spellbound-{Guid.NewGuid():N}?mode=memory&cache=shared";
        keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:DefaultConnection", connectionString));
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

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
        var news = Assert.Single(await Game.GetNewsAsync(mage.Id));
        Assert.StartsWith("You explored", news.Text, StringComparison.Ordinal);
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
        Assert.Empty(await Game.GetNewsAsync(mage.Id));
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
    }

    [Fact]
    public async Task News_log_keeps_the_last_50_reports()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant);

        for (var i = 0; i < GameService.NewsKept + 3; i++)
        {
            await Game.MeditateAsync(Owner, 1);
        }

        var mage = await Game.GetMageAsync(Owner);
        Assert.Equal(GameService.NewsKept, (await Game.GetNewsAsync(mage!.Id)).Count);
    }

    [Fact]
    public async Task Concurrent_saves_of_the_same_mage_cannot_both_spend_turns()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Verdant);
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var rules = factory.Services.GetRequiredService<GameRules>();
        await using var first = await dbFactory.CreateDbContextAsync();
        await using var second = await dbFactory.CreateDbContextAsync();
        var a = await first.Mages.SingleAsync(m => m.OwnerId == Owner);
        var b = await second.Mages.SingleAsync(m => m.OwnerId == Owner);

        a.Explore(100, DateTimeOffset.UtcNow, rules);
        b.Explore(100, DateTimeOffset.UtcNow, rules);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
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

    public void Dispose()
    {
        factory.Dispose();
        keepAlive.Dispose();
    }
}
