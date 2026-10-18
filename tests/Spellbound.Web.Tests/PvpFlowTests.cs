using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Attacks and hexes through the real app: two mages, no protection, a seeded random source.</summary>
public sealed class PvpFlowTests : TestDatabase, IAsyncLifetime
{
    private const string Attacker = "account-1";
    private const string Defender = "account-2";

    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, ProtectionDuration = TimeSpan.Zero, StartingGold = 200_000 };

    private readonly WebApplicationFactory<Program> factory;

    public PvpFlowTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<GameRules>();
            services.AddSingleton(Rules);
            services.RemoveAll<IRandomSource>();
            services.AddSingleton<IRandomSource>(new SeededRandom(7));
        }));
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

    public async Task InitializeAsync() => await factory.Services.GetRequiredService<RoundKeeper>().TickAsync(); // round 1

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> CreateBothAsync()
    {
        await Game.CreateMageAsync(Attacker, "Merlin", MagicSchool.Ember);
        await Game.CreateMageAsync(Defender, "Morgana", MagicSchool.Dawn);
        return (await Game.GetMageAsync(Defender))!.Id;
    }

    [Fact]
    public async Task Attacks_both_ways_at_once_neither_deadlock_nor_lose_land()
    {
        var defenderId = await CreateBothAsync();
        var attackerId = (await Game.GetMageAsync(Attacker))!.Id;
        await Game.RecruitAsync(Attacker, "unit-3", 400);
        await Game.RecruitAsync(Defender, "unit-3", 400);
        var landBefore = (await Game.GetMageAsync(Attacker))!.Land + (await Game.GetMageAsync(Defender))!.Land;

        var reports = await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            Task.Run(() => Game.AttackAsync(Attacker, defenderId)),
            Task.Run(() => Game.AttackAsync(Defender, attackerId)),
        }));

        Assert.DoesNotContain(reports, r => r.Lines[0].StartsWith("Your mage was busy", StringComparison.Ordinal));
        Assert.Contains(reports, r => r.Succeeded);
        var landAfter = (await Game.GetMageAsync(Attacker))!.Land + (await Game.GetMageAsync(Defender))!.Land;
        Assert.Equal(landBefore, landAfter); // every acre taken arrived on the other side
    }

    [Fact]
    public async Task Recruited_army_is_saved()
    {
        await Game.CreateMageAsync(Attacker, "Merlin", MagicSchool.Ember);

        Assert.True((await Game.RecruitAsync(Attacker, "unit-3", 60)).Succeeded);

        var mage = await Game.GetMageAsync(Attacker);
        Assert.Equal(60, mage!.UnitCount("unit-3"));
        Assert.Equal(98, mage.Turns);
    }

    [Fact]
    public async Task Attack_takes_land_and_alerts_the_defender()
    {
        var defenderId = await CreateBothAsync();
        await Game.RecruitAsync(Attacker, "unit-3", 400);

        var report = await Game.AttackAsync(Attacker, defenderId);

        Assert.True(report.Succeeded, string.Join(" ", report.Lines));
        Assert.Equal(275, (await Game.GetMageAsync(Attacker))!.Land);
        Assert.Equal(225, (await Game.GetMageAsync(Defender))!.Land);
        var news = (await Game.GetNewsAsync(Defender))[0];
        Assert.True(news.Hostile);
        Assert.StartsWith("Merlin (Ember) attacked you and broke through", news.Lines[0], StringComparison.Ordinal);
        Assert.Equal(1, await Game.TakeUnseenAlertsAsync(Defender));
        Assert.Equal(0, await Game.TakeUnseenAlertsAsync(Defender));
    }

    [Fact]
    public async Task A_second_win_soon_after_takes_half_as_much_land_after_a_save()
    {
        var defenderId = await CreateBothAsync();
        await Game.RecruitAsync(Attacker, "unit-3", 400);

        Assert.True((await Game.AttackAsync(Attacker, defenderId)).Succeeded);
        var second = await Game.AttackAsync(Attacker, defenderId);

        Assert.True(second.Succeeded, string.Join(" ", second.Lines));
        Assert.Equal(214, (await Game.GetMageAsync(Defender))!.Land); // 225 minus 5% (11 acres)
        Assert.Equal(2, (await Game.GetMageAsync(Defender))!.RecentLosses.Count);
    }

    [Fact]
    public async Task Hex_reaches_the_target_and_rankings_list_both()
    {
        var defenderId = await CreateBothAsync();

        var report = await Game.HexAsync(Attacker, "hex-c", defenderId);

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
