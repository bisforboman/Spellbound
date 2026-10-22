namespace Spellbound.Web.Hosting;

/// <summary>
/// The one background job for games: every minute it ends each game still running past its end, letting the game wrap
/// it up (<see cref="IGameLifecycle.OnGameEndedAsync"/>), then lets the game act on its own deadlines
/// (<see cref="IGameLifecycle.OnTickAsync"/>). The admin page ends a game the same way. Games start on demand,
/// so nothing here starts one, and players never need a per-player job. Safe to run on several hosts at once: the
/// game's row is locked while it is ended, and a game already ended is left alone.
/// </summary>
public sealed class GameKeeper(HostDatabase database, TimeProvider time, IGameLifecycle game, ILogger<GameKeeper> logger)
    : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>Ends every game past its end. Returns the ids of the games it ended.</summary>
    public async Task<IReadOnlyList<long>> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        List<HostedGame> running;
        await using (var db = await database.BeginAsync(cancellationToken))
        {
            running = await db.LoadRunningGamesAsync();
        }

        var ended = new List<long>();
        foreach (var overdue in running.Where(g => g.Round.HasEnded(now)))
        {
            if (await EndAsync(overdue.Id, cancellationToken))
            {
                ended.Add(overdue.Id);
            }
        }

        await game.OnTickAsync(database, now, cancellationToken);
        return ended;
    }

    /// <summary>Ends a running game now, with its wrap-up. False if there is no such game or it had already ended.</summary>
    public async Task<bool> EndAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await database.BeginAsync(cancellationToken);
        if (await db.LoadGameAsync(id, forUpdate: true) is not { IsRunning: true } running)
        {
            return false;
        }

        await game.OnGameEndedAsync(db, running, await db.LoadBansAsync(), cancellationToken);
        await db.EndGameAsync(id, time.GetUtcNow());
        await db.CommitAsync();
        logger.LogInformation("Game {Id} ({Mode} {Number}) ended.", id, running.Mode, running.Round.Number);
        return true;
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
                logger.LogError(ex, "Game check failed; trying again in {Interval}.", CheckEvery);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
