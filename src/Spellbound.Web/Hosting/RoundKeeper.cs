using Microsoft.EntityFrameworkCore;
using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>
/// The round job: starts the first round, and when a round ends, lets the game wrap it up (<see cref="IRoundLifecycle"/>)
/// and starts the next one. Players never need a per-player job. Safe to run on several hosts at once: the round
/// pointer is saved with optimistic concurrency (and created under a fixed key), so only one of them can move it.
/// </summary>
public sealed class RoundKeeper(IDbContextFactory<HostDb> dbs, TimeProvider time, IRoundLifecycle game, ILogger<RoundKeeper> logger)
    : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>Starts or rolls over the round if it's due. Returns the round being played afterwards.</summary>
    public async Task<Round> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        await using var db = await dbs.CreateDbContextAsync(cancellationToken);

        var current = await db.LoadCurrentRoundAsync(cancellationToken);
        if (current is null)
        {
            current = new CurrentRound { Round = Round.First(now, game.RoundLength) };
            db.CurrentRound.Add(current);
            return await SaveAsync(db, current.Round, $"Round 1 started, ends {current.Round.EndsAt:u}.");
        }

        var ended = current.Round;
        if (!ended.HasEnded(now))
        {
            return ended;
        }

        await game.OnRoundEndedAsync(db, ended, await db.LoadBansAsync(cancellationToken), cancellationToken);
        current.Round = ended.Next(now, game.RoundLength);
        return await SaveAsync(
            db,
            current.Round,
            $"Round {ended.Number} ended. Round {current.Round.Number} started, ends {current.Round.EndsAt:u}.");
    }

    /// <summary>Moves the current round's end. An end at or before now ends the round right away (with its reset).</summary>
    public async Task<Round> ChangeEndAsync(DateTimeOffset endsAt, CancellationToken cancellationToken = default)
    {
        await using (var db = await dbs.CreateDbContextAsync(cancellationToken))
        {
            var current = await db.LoadCurrentRoundAsync(cancellationToken)
                          ?? throw new InvalidOperationException("No round has started yet.");
            var old = current.Round.EndsAt;
            current.Round = current.Round.WithEnd(endsAt.ToUniversalTime());
            await db.SaveChangesAsync(cancellationToken); // a reset racing this one makes it throw: try again
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

    private async Task<Round> SaveAsync(HostDb db, Round next, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || HostStore.IsDuplicateKey(ex))
        {
            // Another host moved (or created) the round first. Its result is the one that counts.
            await using var fresh = await dbs.CreateDbContextAsync();
            return (await fresh.LoadCurrentRoundAsync())!.Round;
        }

        logger.LogInformation("{Message}", message);
        return next;
    }
}
