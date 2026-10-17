using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Spellbound.Web.Hosting;

/// <summary>
/// Nightly backups: dumps the whole database (game data and accounts) with pg_dump to a file in Backup:Directory once a
/// day after Backup:TimeOfDayUtc, and keeps the newest Backup:Keep files. pg_dump must be the server's major version or
/// newer (Backup:PgDump and Backup:PgRestore name the tools). Off by default; the Docker image turns it on (see
/// docs/deploy.md). Restoring is <see cref="RestoreIfEmptyAsync"/>, run at startup when Backup:RestoreFrom is set.
/// </summary>
public sealed class BackupKeeper(
    IDbContextFactory<HostDb> dbs, TimeProvider time, IConfiguration configuration, ILogger<BackupKeeper> logger)
    : BackgroundService
{
    /// <summary>Start of every backup file's name (Backup:FilePrefix); the game sets its own, e.g. "spellbound-".</summary>
    public string FilePrefix => configuration["Backup:FilePrefix"] is { Length: > 0 } prefix ? prefix : "backup-";
    public const string FileExtension = ".dump";

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

    /// <summary>Dumps the database now, then removes the oldest backups beyond Backup:Keep.</summary>
    public async Task<string> BackUpNowAsync(CancellationToken cancellationToken = default)
    {
        var directory = Directory ?? throw new InvalidOperationException("Backups are off: Backup:Directory is not set.");
        System.IO.Directory.CreateDirectory(directory);
        var at = time.GetUtcNow();
        var file = Path.Combine(directory, $"{FilePrefix}{at.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{FileExtension}");

        // Dump to a temporary name first, so a crash never leaves a half-written file that looks like a backup.
        var partial = file + ".partial";
        await RunAsync(configuration["Backup:PgDump"] ?? "pg_dump", ["--format=custom", "--no-owner", $"--file={partial}"], cancellationToken);
        File.Move(partial, file, overwrite: true);

        foreach (var (old, _) in Backups(directory).Skip(Keep))
        {
            File.Delete(old);
        }

        logger.LogInformation("Backed up the database to {File}.", file);
        return file;
    }

    /// <summary>
    /// Restores a backup into an empty database (one no migration has run on), for restoring onto a fresh server.
    /// Refuses any other database, so a forgotten Backup:RestoreFrom setting can't overwrite live data. Runs before the
    /// startup migration, which then brings an older backup up to date.
    /// </summary>
    public async Task<bool> RestoreIfEmptyAsync(string backupFile, CancellationToken cancellationToken = default)
    {
        await using (var db = await dbs.CreateDbContextAsync(cancellationToken))
        {
            if (!await HostStore.IsEmptyAsync(db, cancellationToken))
            {
                logger.LogWarning("Backup:RestoreFrom is set, but the database isn't empty. Skipped the restore; remove the setting.");
                return false;
            }
        }

        await RunAsync(
            configuration["Backup:PgRestore"] ?? "pg_restore",
            ["--no-owner", "--exit-on-error", "--single-transaction", backupFile],
            cancellationToken);
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

    /// <summary>Runs pg_dump or pg_restore against the app's database. The password goes in the environment, not the arguments.</summary>
    private async Task RunAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnectionStringBuilder(HostStore.ConnectionString(configuration));
        var start = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add($"--host={connection.Host}");
        start.ArgumentList.Add($"--port={connection.Port}");
        start.ArgumentList.Add($"--username={connection.Username}");
        start.ArgumentList.Add($"--dbname={connection.Database}");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["PGPASSWORD"] = connection.Password ?? string.Empty;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {tool}.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExportTimeout);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{tool} failed ({process.ExitCode}): {(await errors).Trim()}");
        }
    }

    /// <summary>Finished backups in the directory, newest first.</summary>
    private IEnumerable<(string File, DateTimeOffset At)> Backups(string directory) =>
        System.IO.Directory.Exists(directory)
            ? System.IO.Directory.EnumerateFiles(directory, $"{FilePrefix}*{FileExtension}")
                .Select(file => (file, At: ParseTime(file)))
                .Where(b => b.At is not null)
                .Select(b => (b.file, b.At!.Value))
                .OrderByDescending(b => b.Item2)
            : [];

    private DateTimeOffset? ParseTime(string file) =>
        DateTime.TryParseExact(
            Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..],
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var at)
            ? new DateTimeOffset(at, TimeSpan.Zero)
            : null;
}
