using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Round start, end and reset through the real app, with a clock the test moves by hand.</summary>
public sealed class RoundFlowTests : TestDatabase
{
    private const string Merlin = "account-1";
    private const string Morgana = "account-2";

    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, ProtectionDuration = TimeSpan.Zero, StartingGold = 200_000 };

    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> factory;

    public RoundFlowTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<GameRules>();
            services.AddSingleton(Rules);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
        }));
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

    private RoundKeeper Keeper => factory.Services.GetRequiredService<RoundKeeper>();

    [Fact]
    public async Task Ticking_again_before_the_end_keeps_the_round()
    {
        var first = await Keeper.TickAsync();
        time.Advance(TimeSpan.FromDays(27));

        Assert.Equal(first, await Keeper.TickAsync());
    }

    [Fact]
    public async Task Round_end_records_the_hall_of_fame_and_everyone_starts_over()
    {
        await Keeper.TickAsync();
        await Game.CreateMageAsync(Merlin, "Merlin", MagicSchool.Tidal);
        await Game.CreateMageAsync(Morgana, "Morgana", MagicSchool.Ember);
        await Game.ExploreAsync(Merlin, 10);
        var oldMorgana = (await Game.GetMageAsync(Morgana))!.Id;

        time.Advance(Rules.RoundLength);

        // Ended but not yet reset: actions are refused.
        var late = await Game.ExploreAsync(Merlin, 1);
        Assert.False(late.Succeeded);
        Assert.Equal("Round 1 is over. The next round starts in a moment.", late.Lines[0]);

        // Two hosts resetting at once still make only one new round.
        await Task.WhenAll(Keeper.TickAsync(), Keeper.TickAsync());
        var round = await Game.GetRoundAsync();
        Assert.Equal(2, round!.Number);
        Assert.Equal(time.GetUtcNow(), round.StartsAt);

        var fame = Assert.Single(await Game.GetHallOfFameAsync());
        Assert.Equal(1, fame.RoundNumber);
        Assert.Equal(2, fame.MageCount);
        Assert.Equal("Merlin", fame.ByLand[0].Name);

        Assert.Null(await Game.GetMageAsync(Merlin));
        Assert.Empty(await Game.GetNewsAsync(Merlin));
        Assert.Empty(await Game.GetAllMagesAsync());

        Assert.True((await Game.CreateMageAsync(Merlin, "Merlin", MagicSchool.Umbral)).Succeeded);
        Assert.Equal(250, (await Game.GetMageAsync(Merlin))!.Land);

        // A mage id from the last round doesn't reach the owner's new mage.
        await Game.CreateMageAsync(Morgana, "Morgana", MagicSchool.Ember);
        await Game.RecruitAsync(Merlin, "knights", 100);
        var attack = await Game.AttackAsync(Merlin, oldMorgana);
        Assert.False(attack.Succeeded);
        Assert.Null(await Game.GetRivalAsync(oldMorgana));
        Assert.Equal(250, (await Game.GetMageAsync(Morgana))!.Land);
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
