using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>
/// The round job: starts the first round, and when a round ends, lets the game wrap it up (<see cref="IRoundLifecycle"/>)
/// and starts the next one. Players never need a per-player job. Safe to run on several hosts at once: the round row
/// is locked while it moves (and created with a fixed key), so a second host waits and then finds the work done.
/// </summary>
public sealed class RoundKeeper(HostDatabase database, TimeProvider time, IRoundLifecycle game, ILogger<RoundKeeper> logger)
    : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>Starts or rolls over the round if it's due. Returns the round being played afterwards.</summary>
    public async Task<Round> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        await using var db = await database.BeginAsync(cancellationToken);

        var current = await db.LoadCurrentRoundAsync(forUpdate: true);
        if (current is null)
        {
            var first = Round.First(now, game.RoundLength);
            var created = await db.ExecuteAsync(
                "INSERT INTO current_round (id, number, starts_at, ends_at) VALUES (1, $1, $2, $3) ON CONFLICT (id) DO NOTHING",
                first.Number, first.StartsAt, first.EndsAt);
            if (created == 0)
            {
                return (await db.LoadCurrentRoundAsync())!; // another host created it first
            }

            await db.CommitAsync();
            logger.LogInformation("Round 1 started, ends {EndsAt:u}.", first.EndsAt);
            return first;
        }

        if (!current.HasEnded(now))
        {
            return current;
        }

        await game.OnRoundEndedAsync(db, current, await db.LoadBansAsync(), cancellationToken);
        var next = current.Next(now, game.RoundLength);
        await SaveAsync(db, next);
        await db.CommitAsync();
        logger.LogInformation(
            "Round {Ended} ended. Round {Number} started, ends {EndsAt:u}.", current.Number, next.Number, next.EndsAt);
        return next;
    }

    /// <summary>Moves the current round's end. An end at or before now ends the round right away (with its reset).</summary>
    public async Task<Round> ChangeEndAsync(DateTimeOffset endsAt, CancellationToken cancellationToken = default)
    {
        await using (var db = await database.BeginAsync(cancellationToken))
        {
            var current = await db.LoadCurrentRoundAsync(forUpdate: true)
                          ?? throw new InvalidOperationException("No round has started yet.");
            await SaveAsync(db, current.WithEnd(endsAt.ToUniversalTime()));
            await db.CommitAsync();
            logger.LogInformation("Round {Number} end moved from {Old:u} to {New:u}.", current.Number, current.EndsAt, endsAt);
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

    private static Task SaveAsync(DbSession db, Round round) =>
        db.ExecuteAsync(
            "UPDATE current_round SET number = $1, starts_at = $2, ends_at = $3 WHERE id = 1",
            round.Number, round.StartsAt, round.EndsAt);
}
