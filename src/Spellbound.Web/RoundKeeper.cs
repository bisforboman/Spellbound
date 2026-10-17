using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web;

/// <summary>
/// The one background job: starts the first round, and when a round ends, records its hall of fame and starts the
/// next one. Players never need a per-player job. Safe to run on several hosts at once: the round pointer is
/// saved with optimistic concurrency, so only one of them can move it.
/// </summary>
public sealed class RoundKeeper(IDocumentStore store, TimeProvider time, GameRules rules, ILogger<RoundKeeper> logger)
    : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>Starts or rolls over the round if it's due. Returns the round being played afterwards.</summary>
    public async Task<Round> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        using var session = store.OpenAsyncSession();
        session.Advanced.OptimisticConcurrencyMode = OptimisticConcurrencyMode.Writes;

        var current = await session.LoadAsync<CurrentRound>(RavenSetup.CurrentRoundKey, cancellationToken);
        if (current is null)
        {
            current = new CurrentRound { Round = Round.First(now, rules) };
            await session.StoreAsync(current, changeVector: string.Empty, RavenSetup.CurrentRoundKey, cancellationToken);
            return await SaveAsync(session, current.Round, $"Round 1 started, ends {current.Round.EndsAt:u}.");
        }

        var ended = current.Round;
        if (!ended.HasEnded(now))
        {
            return ended;
        }

        var mages = await RavenSetup.LoadRoundMagesAsync(session, ended.Number);
        var fame = HallOfFame.Record(ended, [.. mages], rules);
        await session.StoreAsync(fame, changeVector: string.Empty, RavenSetup.HallOfFameKey(ended.Number), cancellationToken);
        current.Round = ended.Next(now, rules);
        return await SaveAsync(
            session,
            current.Round,
            $"Round {ended.Number} ended with {mages.Count} mages. Round {current.Round.Number} started, ends {current.Round.EndsAt:u}.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckEvery, time);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Round check failed; trying again in {Interval}.", CheckEvery);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<Round> SaveAsync(IAsyncDocumentSession session, Round next, string message)
    {
        try
        {
            await session.SaveChangesAsync();
        }
        catch (ConcurrencyException)
        {
            // Another host moved the round first. Its result is the one that counts.
            using var fresh = store.OpenAsyncSession();
            return (await fresh.LoadAsync<CurrentRound>(RavenSetup.CurrentRoundKey)).Round;
        }

        logger.LogInformation("{Message}", message);
        return next;
    }
}
