using System.Globalization;
using Raven.Client.Documents;
using Raven.Client.Documents.Smuggler;
using Spellbound.Web.Data;

namespace Spellbound.Web;

/// <summary>
/// Nightly backups: exports the whole database (game data and accounts) to a dump file in Backup:Directory once a day
/// after Backup:TimeOfDayUtc, and keeps the newest Backup:Keep files. Uses RavenDB's export rather than its backup task,
/// which may not be available on every licence. Off by default; the Docker image turns it on (see docs/deploy.md).
/// Restoring is <see cref="RestoreIfEmptyAsync"/>, run at startup when Backup:RestoreFrom is set.
/// </summary>
public sealed class BackupKeeper(IDocumentStore store, TimeProvider time, IConfiguration configuration, ILogger<BackupKeeper> logger)
    : BackgroundService
{
    public const string FilePrefix = "spellbound-";
    public const string FileExtension = ".ravendbdump";

    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(30);

    public string? Directory => configuration["Backup:Directory"] is { Length: > 0 } directory ? directory : null;

    private int Keep => Math.Max(1, configuration.GetValue("Backup:Keep", 7));

    private TimeSpan TimeOfDay => configuration.GetValue("Backup:TimeOfDayUtc", TimeSpan.FromHours(3));

    /// <summary>The newest backup's time (from its file name), or null when there is none.</summary>
    public DateTimeOffset? LastBackupAt() => Directory is { } directory ? Backups(directory).FirstOrDefault().At : null;

    /// <summary>Whether a day's backup is due: past the time of day, and none taken since then.</summary>
    public static bool IsDue(DateTimeOffset? lastBackupAt, DateTimeOffset now, TimeSpan timeOfDayUtc)
    {
        var todaysSlot = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero) + timeOfDayUtc;
        var latestSlot = now >= todaysSlot ? todaysSlot : todaysSlot.AddDays(-1);
        return lastBackupAt is null || lastBackupAt < latestSlot;
    }

    /// <summary>Takes a backup if one is due. Returns the new file, or null if none was due or backups are off.</summary>
    public async Task<string?> TickAsync(CancellationToken cancellationToken = default) =>
        Directory is not null && IsDue(LastBackupAt(), time.GetUtcNow(), TimeOfDay)
            ? await BackUpNowAsync(cancellationToken)
            : null;

    /// <summary>Exports the database now, then removes the oldest backups beyond Backup:Keep.</summary>
    public async Task<string> BackUpNowAsync(CancellationToken cancellationToken = default)
    {
        var directory = Directory ?? throw new InvalidOperationException("Backups are off: Backup:Directory is not set.");
        System.IO.Directory.CreateDirectory(directory);
        var at = time.GetUtcNow();
        var file = Path.Combine(directory, $"{FilePrefix}{at.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{FileExtension}");

        // Export to a temporary name first, so a crash never leaves a half-written file that looks like a backup.
        var partial = file + ".partial";
        var export = await store.Smuggler.ExportAsync(new DatabaseSmugglerExportOptions(), partial, cancellationToken);
        await export.WaitForCompletionAsync(ExportTimeout);
        File.Move(partial, file, overwrite: true);

        foreach (var (old, _) in Backups(directory).Skip(Keep))
        {
            File.Delete(old);
        }

        logger.LogInformation("Backed up the database to {File}.", file);
        return file;
    }

    /// <summary>
    /// Imports a backup into an empty database, for restoring onto a fresh data volume. Refuses a database that already
    /// has a round, so a forgotten Backup:RestoreFrom setting can't overwrite live data.
    /// </summary>
    public async Task<bool> RestoreIfEmptyAsync(string backupFile, CancellationToken cancellationToken = default)
    {
        using (var session = store.OpenAsyncSession())
        {
            if (await session.Advanced.ExistsAsync(RavenSetup.CurrentRoundKey, cancellationToken))
            {
                logger.LogWarning("Backup:RestoreFrom is set, but the database isn't empty. Skipped the restore; remove the setting.");
                return false;
            }
        }

        var import = await store.Smuggler.ImportAsync(new DatabaseSmugglerImportOptions(), backupFile, cancellationToken);
        await import.WaitForCompletionAsync(ExportTimeout);
        logger.LogWarning("Restored the database from {File}. Remove Backup:RestoreFrom before the next start.", backupFile);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Directory is null)
        {
            return;
        }

        using var timer = new PeriodicTimer(CheckEvery, time);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Backup failed; trying again in {Interval}.", CheckEvery);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Finished backups in the directory, newest first.</summary>
    private static IEnumerable<(string File, DateTimeOffset At)> Backups(string directory) =>
        System.IO.Directory.Exists(directory)
            ? System.IO.Directory.EnumerateFiles(directory, $"{FilePrefix}*{FileExtension}")
                .Select(file => (file, At: ParseTime(file)))
                .Where(b => b.At is not null)
                .Select(b => (b.file, b.At!.Value))
                .OrderByDescending(b => b.Item2)
            : [];

    private static DateTimeOffset? ParseTime(string file) =>
        DateTime.TryParseExact(
            Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..],
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var at)
            ? new DateTimeOffset(at, TimeSpan.Zero)
            : null;
}
