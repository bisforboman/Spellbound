using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Client.Documents;
using Raven.Embedded;
using Raven.TestDriver;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>Attacks and hexes through the real app: two mages, no protection, a seeded random source.</summary>
public sealed class PvpFlowTests : RavenTestDriver
{
    private const string Attacker = "ApplicationUsers/1-A";
    private const string Defender = "ApplicationUsers/2-A";

    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, StartingGold = 200_000 };

    private readonly IDocumentStore store;
    private readonly WebApplicationFactory<Program> factory;

    static PvpFlowTests()
    {
        ConfigureServer(new Raven.TestDriver.TestServerOptions
        {
            Licensing = new ServerOptions.LicensingOptions { ThrowOnInvalidOrMissingLicense = false },
        });
    }

    public PvpFlowTests()
    {
        store = GetDocumentStore();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDocumentStore>();
            services.AddSingleton(store);
            services.RemoveAll<GameRules>();
            services.AddSingleton(Rules);
            services.RemoveAll<IRandomSource>();
            services.AddSingleton<IRandomSource>(new SeededRandom(7));
        }));
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

    protected override void PreInitialize(IDocumentStore documentStore) =>
        documentStore.Conventions.FindIdentityProperty = RavenSetup.IsDocumentKey;

    private async Task<Guid> CreateBothAsync()
    {
        await Game.CreateMageAsync(Attacker, "Merlin", MagicSchool.Tidal);
        await Game.CreateMageAsync(Defender, "Morgana", MagicSchool.Radiant);
        WaitForIndexing(store);
        return (await Game.GetMageAsync(Defender))!.Id;
    }

    [Fact]
    public async Task Recruited_army_is_saved()
    {
        await Game.CreateMageAsync(Attacker, "Merlin", MagicSchool.Tidal);

        Assert.True((await Game.RecruitAsync(Attacker, "knights", 60)).Succeeded);

        var mage = await Game.GetMageAsync(Attacker);
        Assert.Equal(60, mage!.UnitCount("knights"));
        Assert.Equal(98, mage.Turns);
    }

    [Fact]
    public async Task Attack_takes_land_and_alerts_the_defender()
    {
        var defenderId = await CreateBothAsync();
        await Game.RecruitAsync(Attacker, "knights", 400);

        var report = await Game.AttackAsync(Attacker, defenderId);

        Assert.True(report.Succeeded, string.Join(" ", report.Lines));
        Assert.Equal(275, (await Game.GetMageAsync(Attacker))!.Land);
        Assert.Equal(225, (await Game.GetMageAsync(Defender))!.Land);
        var news = (await Game.GetNewsAsync(Defender))[0];
        Assert.True(news.Hostile);
        Assert.StartsWith("Merlin (Tidal) attacked you and broke through", news.Lines[0], StringComparison.Ordinal);
        Assert.Equal(1, await Game.TakeUnseenAlertsAsync(Defender));
        Assert.Equal(0, await Game.TakeUnseenAlertsAsync(Defender));
    }

    [Fact]
    public async Task Hex_reaches_the_target_and_rankings_list_both()
    {
        var defenderId = await CreateBothAsync();

        var report = await Game.HexAsync(Attacker, "mana-drain", defenderId);

        Assert.True(report.Succeeded, string.Join(" ", report.Lines));
        Assert.True((await Game.GetNewsAsync(Defender))[0].Hostile);
        Assert.Equal(2, (await Game.GetAllMagesAsync()).Count);
        Assert.Equal("Morgana", (await Game.GetRivalAsync(defenderId))!.Name);
    }

    [Fact]
    public async Task Failed_attack_changes_nothing_for_either_mage()
    {
        var defenderId = await CreateBothAsync();

        Assert.False((await Game.AttackAsync(Attacker, defenderId)).Succeeded); // no army
        Assert.False((await Game.AttackAsync(Attacker, Guid.NewGuid())).Succeeded); // no such mage

        Assert.Equal(100, (await Game.GetMageAsync(Attacker))!.Turns);
        Assert.Empty(await Game.GetNewsAsync(Attacker));
        Assert.Empty(await Game.GetNewsAsync(Defender));
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }
}
