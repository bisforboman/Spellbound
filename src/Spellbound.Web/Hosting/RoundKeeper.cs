using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>
/// The round job: starts the first round, and when a round ends, lets the game wrap it up (<see cref="IRoundLifecycle"/>)
/// and starts the next one. Players never need a per-player job. Safe to run on several hosts at once: the round
/// pointer is saved with optimistic concurrency, so only one of them can move it.
/// </summary>
public sealed class RoundKeeper(IDocumentStore store, TimeProvider time, IRoundLifecycle game, ILogger<RoundKeeper> logger)
    : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>Starts or rolls over the round if it's due. Returns the round being played afterwards.</summary>
    public async Task<Round> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        using var session = store.OpenAsyncSession();
        session.Advanced.OptimisticConcurrencyMode = OptimisticConcurrencyMode.Writes;

        var current = await session.LoadAsync<CurrentRound>(HostStore.CurrentRoundKey, cancellationToken);
        if (current is null)
        {
            current = new CurrentRound { Round = Round.First(now, game.RoundLength) };
            await session.StoreAsync(current, changeVector: string.Empty, HostStore.CurrentRoundKey, cancellationToken);
            return await SaveAsync(session, current.Round, $"Round 1 started, ends {current.Round.EndsAt:u}.");
        }

        var ended = current.Round;
        if (!ended.HasEnded(now))
        {
            return ended;
        }

        await game.OnRoundEndedAsync(session, ended, await HostStore.LoadBansAsync(session), cancellationToken);
        current.Round = ended.Next(now, game.RoundLength);
        return await SaveAsync(
            session,
            current.Round,
            $"Round {ended.Number} ended. Round {current.Round.Number} started, ends {current.Round.EndsAt:u}.");
    }

    /// <summary>Moves the current round's end. An end at or before now ends the round right away (with its reset).</summary>
    public async Task<Round> ChangeEndAsync(DateTimeOffset endsAt, CancellationToken cancellationToken = default)
    {
        using (var session = store.OpenAsyncSession())
        {
            session.Advanced.OptimisticConcurrencyMode = OptimisticConcurrencyMode.Writes;
            var current = await session.LoadAsync<CurrentRound>(HostStore.CurrentRoundKey, cancellationToken)
                          ?? throw new InvalidOperationException("No round has started yet.");
            var old = current.Round.EndsAt;
            current.Round = current.Round.WithEnd(endsAt);
            await session.SaveChangesAsync(cancellationToken); // a reset racing this one makes it throw: try again
            logger.LogInformation("Round {Number} end moved from {Old:u} to {New:u}.", current.Round.Number, old, endsAt);
        }

        return await TickAsync(cancellationToken);
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
            return (await fresh.LoadAsync<CurrentRound>(HostStore.CurrentRoundKey)).Round;
        }

        logger.LogInformation("{Message}", message);
        return next;
    }
}
