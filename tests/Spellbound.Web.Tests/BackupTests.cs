using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Nightly backup scheduling, pg_dump, retention and restore, against real PostgreSQL databases.</summary>
public sealed class BackupTests : TestDatabase
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ThreeAm = TimeSpan.FromHours(3);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "spellbound-backups-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime time = new(T0);

    [Theory]
    [InlineData(null, "2026-10-17T12:00Z", true)] // never backed up
    [InlineData("2026-10-17T03:05Z", "2026-10-17T12:00Z", false)] // today's done
    [InlineData("2026-10-16T03:05Z", "2026-10-17T12:00Z", true)] // yesterday's only, and it's past 03:00
    [InlineData("2026-10-16T03:05Z", "2026-10-17T02:59Z", false)] // not 03:00 yet today
    [InlineData("2026-10-15T03:05Z", "2026-10-17T02:59Z", true)] // missed yesterday's
    public void A_backup_is_due_once_a_day_after_the_time_of_day(string? last, string now, bool due)
    {
        DateTimeOffset? lastAt = last is null ? null : DateTimeOffset.Parse(last, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(due, BackupKeeper.IsDue(lastAt, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), ThreeAm));
    }

    [Fact]
    public async Task Backups_export_everything_keep_the_newest_and_restore_into_an_empty_database()
    {
        await SeedAsync();
        var keeper = Keeper(ConnectionString, keep: 2);

        var first = await keeper.TickAsync();
        Assert.NotNull(first);
        Assert.Null(await keeper.TickAsync()); // already done today
        time.Advance(TimeSpan.FromDays(1));
        await keeper.TickAsync();
        time.Advance(TimeSpan.FromDays(1));
        var newest = await keeper.TickAsync();

        var files = Directory.GetFiles(directory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(2, files.Count); // the first one was removed
        Assert.DoesNotContain(Path.GetFileName(first), files);
        Assert.Equal(time.GetUtcNow(), keeper.LastBackupAt());

        var fresh = CreateDatabase(); // empty: no migration has run
        Assert.True(await Keeper(fresh).RestoreIfEmptyAsync(newest!));
        Assert.Equal("Merlin", (await WithDbAsync(db => db.LoadMageAsync(1, "account-1"), fresh))!.Name);
        Assert.NotNull(await WithDbAsync(db => db.LoadRunningGameAsync(SpellboundHost.RealmMode), fresh));
        Assert.Empty(await SchemaMigrator.MigrateAsync(fresh, [HostStore.Schema, new SpellboundSchema().Schema])); // up to date

        // Once there's data, a restore is refused, so a leftover setting can't overwrite a live game.
        Assert.False(await Keeper(fresh).RestoreIfEmptyAsync(newest!));
        Assert.False(await Keeper(CreateDatabase(MigratedTemplate)).RestoreIfEmptyAsync(newest!)); // migrated, even with no rows
    }

    [Fact]
    public async Task A_backup_over_a_day_old_is_reported_as_a_problem()
    {
        await SeedAsync();
        var keeper = Keeper(ConnectionString);
        await keeper.BackUpNowAsync();

        Assert.False(keeper.CheckOverdue());
        time.Advance(BackupKeeper.OverdueAfter + TimeSpan.FromMinutes(1));
        Assert.True(keeper.CheckOverdue()); // logged as an error, which the alerts pick up
        time.Advance(TimeSpan.FromHours(12));
        Assert.False(keeper.CheckOverdue()); // reminds once a day, not every check
        time.Advance(TimeSpan.FromHours(13));
        Assert.True(keeper.CheckOverdue());
    }

    [Fact]
    public async Task Backups_are_off_without_a_directory()
    {
        var keeper = new BackupKeeper(time, new ConfigurationBuilder().Build(), NullLogger<BackupKeeper>.Instance);

        Assert.Null(keeper.Directory);
        Assert.Null(await keeper.TickAsync());
        Assert.Null(keeper.LastBackupAt());
    }

    public override void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        base.Dispose();
    }

    private BackupKeeper Keeper(string connectionString, int keep = 7) =>
        new(time, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{HostStore.ConnectionStringName}"] = connectionString,
            ["Backup:Directory"] = directory,
            ["Backup:Keep"] = keep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Backup:TimeOfDayUtc"] = "03:00:00",
        }).Build(), NullLogger<BackupKeeper>.Instance);

    private Task SeedAsync() => WithDbAsync(async db =>
    {
        var round = Round.First(T0, GameRules.Default.RoundLength);
        await db.ExecuteAsync(
            "INSERT INTO games (id, mode, number, starts_at, ends_at, rules, created_at) VALUES (1, $1, $2, $3, $4, '{}', $3)",
            SpellboundHost.RealmMode, round.Number, round.StartsAt, round.EndsAt);
        return await db.InsertMageAsync(1, Mage.Create("account-1", "Merlin", MagicSchool.Tidal, T0, GameRules.Default));
    });

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
