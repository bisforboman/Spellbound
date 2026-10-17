using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Raven.Client.Documents;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>Nightly backup scheduling, export, retention and restore, against real in-memory databases.</summary>
public sealed class BackupTests : SpellboundTestDriver
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
        using var live = GetDocumentStore();
        await SeedAsync(live);
        var keeper = Keeper(live, keep: 2);

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

        using var fresh = GetDocumentStore();
        Assert.True(await Keeper(fresh).RestoreIfEmptyAsync(newest!));
        using (var session = fresh.OpenAsyncSession())
        {
            Assert.Equal("Merlin", (await session.LoadAsync<Mage>(RavenSetup.MageKey(1, "ApplicationUsers/1-A"))).Name);
            Assert.NotNull(await session.LoadAsync<CurrentRound>(RavenSetup.CurrentRoundKey));
        }

        // Once there's data, a restore is refused, so a leftover setting can't overwrite a live game.
        Assert.False(await Keeper(fresh).RestoreIfEmptyAsync(newest!));
    }

    [Fact]
    public async Task Backups_are_off_without_a_directory()
    {
        using var live = GetDocumentStore();
        var keeper = new BackupKeeper(live, time, new ConfigurationBuilder().Build(), NullLogger<BackupKeeper>.Instance);

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

    private BackupKeeper Keeper(IDocumentStore store, int keep = 7) =>
        new(store, time, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backup:Directory"] = directory,
            ["Backup:Keep"] = keep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Backup:TimeOfDayUtc"] = "03:00:00",
        }).Build(), NullLogger<BackupKeeper>.Instance);

    private static async Task SeedAsync(IDocumentStore store)
    {
        using var session = store.OpenAsyncSession();
        await session.StoreAsync(new CurrentRound { Round = Round.First(T0, GameRules.Default) }, RavenSetup.CurrentRoundKey);
        await session.StoreAsync(
            Mage.Create("ApplicationUsers/1-A", "Merlin", MagicSchool.Tidal, T0, GameRules.Default),
            RavenSetup.MageKey(1, "ApplicationUsers/1-A"));
        await session.SaveChangesAsync();
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
