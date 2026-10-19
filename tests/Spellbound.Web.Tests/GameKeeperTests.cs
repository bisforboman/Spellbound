using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Games ending at their end, through the real app, with a clock the test moves by hand.</summary>
public sealed class GameKeeperTests : TestDatabase
{
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> factory;

    public GameKeeperTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
        }));
    }

    private GameKeeper Keeper => factory.Services.GetRequiredService<GameKeeper>();

    private MatchStore Matches => factory.Services.GetRequiredService<MatchStore>();

    [Fact]
    public async Task Nothing_starts_on_its_own_and_a_game_runs_until_its_end()
    {
        Assert.Empty(await Keeper.TickAsync());
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM games"));

        var id = await Matches.CreateAsync([null, null], GameRules.Default, time.GetUtcNow());
        time.Advance(MatchStore.OpenFor - TimeSpan.FromMinutes(1));

        Assert.Empty(await Keeper.TickAsync());
        Assert.True((await WithDbAsync(db => db.LoadGameAsync(id)))!.IsRunning);
    }

    [Fact]
    public async Task A_game_past_its_end_is_ended_once()
    {
        var id = await Matches.CreateAsync([null, null], GameRules.Default, time.GetUtcNow());
        time.Advance(MatchStore.OpenFor);

        // Two hosts checking at once still end it only once.
        var ended = await Task.WhenAll(Keeper.TickAsync(), Keeper.TickAsync());

        Assert.Equal([id], ended.SelectMany(e => e));
        Assert.Equal(time.GetUtcNow(), (await WithDbAsync(db => db.LoadGameAsync(id)))!.EndedAt);
        Assert.False(await Keeper.EndAsync(id));
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
