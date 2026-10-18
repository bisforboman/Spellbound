using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Domain;
using Spellbound.Web;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Runs the real app against a fresh PostgreSQL database per test.</summary>
public sealed class GameFlowTests : TestDatabase, IAsyncLifetime
{
    private const string Owner = "account-1";

    private readonly WebApplicationFactory<Program> factory;

    public GameFlowTests()
    {
        factory = CreateApp();
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

    public async Task InitializeAsync() => await factory.Services.GetRequiredService<RoundKeeper>().TickAsync(); // round 1

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Action_spends_turns_saves_the_mage_and_writes_news()
    {
        Assert.True((await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Grove)).Succeeded);

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
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Grove);

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
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Grove);

        Assert.True((await Game.CastAsync(Owner, "self-b")).Succeeded);

        var mage = await Game.GetMageAsync(Owner);
        var effect = Assert.Single(mage!.ActiveEffects);
        Assert.Equal("self-b", effect.SpellId);
    }

    [Fact]
    public async Task Second_mage_for_the_same_player_is_refused()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Grove);

        Assert.False((await Game.CreateMageAsync(Owner, "Morgana", MagicSchool.Veil)).Succeeded);
        Assert.Equal("Merlin", (await Game.GetMageAsync(Owner))!.Name);
    }

    [Fact]
    public async Task News_log_keeps_the_last_50_reports()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Grove);

        for (var i = 0; i < GameService.NewsKept + 3; i++)
        {
            await Game.MeditateAsync(Owner, 1);
        }

        Assert.Equal(GameService.NewsKept, (await Game.GetNewsAsync(Owner)).Count);
    }

    [Fact]
    public async Task Taps_at_the_same_moment_wait_for_each_other_and_never_spend_a_turn_twice()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Grove); // 100 turns

        var reports = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(() => Game.ExploreAsync(Owner, 5))));

        Assert.Equal(20, reports.Count(r => r.Succeeded)); // 20 × 5 turns
        Assert.All(reports.Where(r => !r.Succeeded), r => Assert.StartsWith("That takes 5 turns", r.Lines[0], StringComparison.Ordinal));
        Assert.Equal(0, (await Game.GetMageAsync(Owner))!.Turns);
        Assert.Equal(20, (await Game.GetNewsAsync(Owner)).Count);
    }

    [Fact]
    public async Task An_action_waiting_too_long_for_a_locked_mage_is_busy_and_changes_nothing()
    {
        await Game.CreateMageAsync(Owner, "Merlin", MagicSchool.Grove);
        await using var source = Npgsql.NpgsqlDataSource.Create(ConnectionString);
        await using (var other = await new HostDatabase(source).BeginAsync())
        {
            await other.LoadMageAsync(1, Owner, forUpdate: true); // held, never committed

            var report = await Game.ExploreAsync(Owner, 1); // waits for lock_timeout

            Assert.StartsWith("Your mage was busy", report.Lines[0], StringComparison.Ordinal);
        }

        Assert.Equal(100, (await Game.GetMageAsync(Owner))!.Turns);
        Assert.Empty(await Game.GetNewsAsync(Owner));
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
