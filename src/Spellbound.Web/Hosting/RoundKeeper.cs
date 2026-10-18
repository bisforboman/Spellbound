using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>
/// The round job: keeps the game's always-on game running (<see cref="IRoundLifecycle.NextAlwaysOn"/>). It starts the
/// first one, and when one ends, lets the game wrap it up (<see cref="IRoundLifecycle.OnGameEndedAsync"/>) and starts the
/// next. Players never need a per-player job. Safe to run on several hosts at once: a transaction-scoped advisory lock
/// per mode makes a second host wait and then find the work done.
/// </summary>
public sealed class RoundKeeper(HostDatabase database, TimeProvider time, IRoundLifecycle game, ILogger<RoundKeeper> logger)
    : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>Starts or rolls over the always-on game if it's due. Returns the game running afterwards.</summary>
    public async Task<HostedGame> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        await using var db = await database.BeginAsync(cancellationToken);
        await db.LockGamesOfModeAsync(game.AlwaysOnMode);

        var current = await db.LoadRunningGameAsync(game.AlwaysOnMode, forUpdate: true);
        if (current is not null && !current.Round.HasEnded(now))
        {
            return current;
        }

        if (current is not null)
        {
            await game.OnGameEndedAsync(db, current, await db.LoadBansAsync(), cancellationToken);
            await db.EndGameAsync(current.Id, now);
        }

        var next = await db.InsertGameAsync(game.NextAlwaysOn(), now);
        await db.CommitAsync();
        if (current is null)
        {
            logger.LogInformation("Round {Number} started, ends {EndsAt:u}.", next.Round.Number, next.Round.EndsAt);
        }
        else
        {
            logger.LogInformation(
                "Round {Ended} ended. Round {Number} started, ends {EndsAt:u}.",
                current.Round.Number, next.Round.Number, next.Round.EndsAt);
        }

        return next;
    }

    /// <summary>
    /// Moves the always-on game's end. An end at or before now ends it right away (with its wrap-up) and starts the next.
    /// </summary>
    public async Task<HostedGame> ChangeEndAsync(DateTimeOffset endsAt, CancellationToken cancellationToken = default)
    {
        await using (var db = await database.BeginAsync(cancellationToken))
        {
            await db.LockGamesOfModeAsync(game.AlwaysOnMode);
            var current = await db.LoadRunningGameAsync(game.AlwaysOnMode, forUpdate: true)
                          ?? throw new InvalidOperationException("No round has started yet.");
            var moved = current.Round.WithEnd(endsAt.ToUniversalTime());
            await db.ExecuteAsync("UPDATE games SET ends_at = $2 WHERE id = $1", current.Id, moved.EndsAt);
            await db.CommitAsync();
            logger.LogInformation(
                "Round {Number} end moved from {Old:u} to {New:u}.", current.Round.Number, current.Round.EndsAt, endsAt);
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
}
