namespace Spellbound.Web;

/// <summary>
/// Plays Skirmish rounds whose time limit has run out, whether or not everyone is ready. One job for every game,
/// checking each minute: there are no per-game timers. Rounds where everyone readied up have already played.
/// </summary>
public sealed class SkirmishKeeper(SkirmishService skirmishes, TimeProvider time, ILogger<SkirmishKeeper> logger) : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckEvery, time);
        do
        {
            try
            {
                await skirmishes.PlayDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Skirmish round check failed; trying again in {Interval}.", CheckEvery);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
