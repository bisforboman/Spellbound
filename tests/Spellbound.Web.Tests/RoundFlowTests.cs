using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Round start, end and reset through the real app, with a clock the test moves by hand.</summary>
public sealed class RoundFlowTests : TestDatabase
{
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> factory;

    public RoundFlowTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
        }));
    }

    private PlaceholderGame Game => factory.Services.GetRequiredService<PlaceholderGame>();

    private RoundKeeper Keeper => factory.Services.GetRequiredService<RoundKeeper>();

    [Fact]
    public async Task Ticking_again_before_the_end_keeps_the_round()
    {
        var first = await Keeper.TickAsync();
        time.Advance(TimeSpan.FromDays(27));

        Assert.Equal(first, await Keeper.TickAsync());
    }

    [Fact]
    public async Task When_a_round_ends_the_next_starts_once()
    {
        await Keeper.TickAsync();
        Assert.Equal(1, (await Game.GetRoundAsync())!.Number);

        time.Advance(PlaceholderGame.RoundLength);

        // Two hosts resetting at once still make only one new round.
        await Task.WhenAll(Keeper.TickAsync(), Keeper.TickAsync());
        var round = await Game.GetRoundAsync();
        Assert.Equal(2, round!.Number);
        Assert.Equal(time.GetUtcNow(), round.StartsAt);
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
