using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web;

/// <summary>
/// Thin glue between pages and the domain: load the mage, run one domain action, save.
/// Opens a session per call (a Blazor Server circuit outlives one action) and only loads by key,
/// because RavenDB loads are always consistent while queries may lag behind writes.
/// </summary>
public sealed class GameService(IDocumentStore store, TimeProvider time, GameRules rules, IRandomSource random)
{
    public const int NewsKept = 50;

    public GameRules Rules => rules;

    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>Read-only copy of the player's mage with turns accrued to now (not saved).</summary>
    public async Task<Mage?> GetMageAsync(string ownerId)
    {
        using var session = store.OpenAsyncSession();
        var mage = await session.LoadAsync<Mage>(RavenSetup.MageKey(ownerId));
        mage?.AccrueTurns(Now, rules);
        return mage;
    }

    public async Task<IReadOnlyList<NewsEntry>> GetNewsAsync(string ownerId)
    {
        using var session = store.OpenAsyncSession();
        var log = await session.LoadAsync<NewsLog>(RavenSetup.NewsKey(ownerId));
        return log?.Entries ?? [];
    }

    /// <summary>How many hostile news entries arrived since the player last looked, and resets the count.</summary>
    public async Task<int> TakeUnseenAlertsAsync(string ownerId)
    {
        using var session = store.OpenAsyncSession();
        var key = RavenSetup.NewsKey(ownerId);
        var log = await session.LoadAsync<NewsLog>(key);
        if (log is not { Unseen: > 0 })
        {
            return 0;
        }

        var unseen = log.Unseen; // read first: saving refreshes the loaded log with the patched value
        session.Advanced.Patch<NewsLog, int>(key, l => l.Unseen, 0); // a patch can't overwrite news written meanwhile
        await session.SaveChangesAsync();
        return unseen;
    }

    /// <summary>
    /// Every mage, for rankings and picking targets. A query, so a mage created a moment ago may be missing.
    /// Sorted in memory: fine for one server's players; add an index if it grows past a few thousand.
    /// </summary>
    public async Task<IReadOnlyList<Mage>> GetAllMagesAsync()
    {
        using var session = store.OpenAsyncSession();
        return await session.Query<Mage>().ToListAsync();
    }

    /// <summary>Another mage by its public id (never the owner id, which is an account key).</summary>
    public async Task<Mage?> GetRivalAsync(Guid mageId)
    {
        using var session = store.OpenAsyncSession();
        return await session.Query<Mage>().Where(m => m.Id == mageId).FirstOrDefaultAsync();
    }

    public async Task<ActionReport> CreateMageAsync(string ownerId, string name, MagicSchool school)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < Mage.MinNameLength or > Mage.MaxNameLength)
        {
            return ActionReport.Fail($"Your name must be {Mage.MinNameLength}–{Mage.MaxNameLength} characters.");
        }

        if (!Enum.IsDefined(school))
        {
            return ActionReport.Fail("Choose a school of magic.");
        }

        var mage = Mage.Create(ownerId, trimmed, school, Now, rules);
        using var session = store.OpenAsyncSession();
        await session.StoreAsync(mage, changeVector: string.Empty, RavenSetup.MageKey(ownerId)); // empty = must not exist yet
        try
        {
            await session.SaveChangesAsync();
        }
        catch (ConcurrencyException)
        {
            return ActionReport.Fail("You already have a mage.");
        }

        return new ActionReport(true, 0, [$"{mage.Name} of the {mage.School} school awakens."]);
    }

    public Task<ActionReport> ExploreAsync(string ownerId, int turns) =>
        ActAsync(ownerId, (mage, now) => mage.Explore(turns, now, rules));

    public Task<ActionReport> BuildAsync(string ownerId, BuildingType type, int count) =>
        ActAsync(ownerId, (mage, now) => mage.Build(type, count, now, rules));

    public Task<ActionReport> MeditateAsync(string ownerId, int turns) =>
        ActAsync(ownerId, (mage, now) => mage.Meditate(turns, now, rules));

    public Task<ActionReport> CastAsync(string ownerId, string spellId) =>
        ActAsync(ownerId, (mage, now) => mage.Cast(spellId, now, rules));

    public Task<ActionReport> RecruitAsync(string ownerId, string unitId, int count) =>
        ActAsync(ownerId, (mage, now) => mage.Recruit(unitId, count, now, rules));

    public Task<ActionReport> AttackAsync(string ownerId, Guid targetId) =>
        HostileAsync(ownerId, targetId, (mage, target, now) => mage.Attack(target, now, rules, random));

    public Task<ActionReport> HexAsync(string ownerId, string hexId, Guid targetId) =>
        HostileAsync(ownerId, targetId, (mage, target, now) => mage.CastHex(hexId, target, now, rules, random));

    private async Task<ActionReport> ActAsync(string ownerId, Func<Mage, DateTimeOffset, ActionReport> action)
    {
        using var session = store.OpenAsyncSession();
        session.Advanced.OptimisticConcurrencyMode = OptimisticConcurrencyMode.Writes; // a double-tap can't spend turns twice

        var mage = await session.LoadAsync<Mage>(RavenSetup.MageKey(ownerId));
        if (mage is null)
        {
            return ActionReport.Fail("You don't have a mage yet.");
        }

        var now = Now;
        var report = action(mage, now);
        if (!report.Succeeded)
        {
            return report;
        }

        await AddNewsAsync(session, ownerId, new NewsEntry(now, report.Lines));
        return await SaveAsync(session, report);
    }

    /// <summary>
    /// Like <see cref="ActAsync"/>, but changes two mages. Both are loaded by key in one session, so if either
    /// of them acts at the same moment, the save fails and nothing happens.
    /// </summary>
    private async Task<ActionReport> HostileAsync(
        string ownerId, Guid targetId, Func<Mage, Mage, DateTimeOffset, HostileReport> action)
    {
        using var session = store.OpenAsyncSession();
        session.Advanced.OptimisticConcurrencyMode = OptimisticConcurrencyMode.Writes;

        var mage = await session.LoadAsync<Mage>(RavenSetup.MageKey(ownerId));
        if (mage is null)
        {
            return ActionReport.Fail("You don't have a mage yet.");
        }

        // The query only finds the target's owner; the mage itself is then loaded by key, which is always current.
        var targetOwner = await session.Query<Mage>()
            .Where(m => m.Id == targetId)
            .Select(m => m.OwnerId)
            .FirstOrDefaultAsync();
        var target = targetOwner is null ? null
            : targetOwner == ownerId ? mage
            : await session.LoadAsync<Mage>(RavenSetup.MageKey(targetOwner));
        if (target is null)
        {
            return ActionReport.Fail("There is no such mage.");
        }

        var now = Now;
        var result = action(mage, target, now);
        if (!result.Report.Succeeded)
        {
            return result.Report;
        }

        await AddNewsAsync(session, ownerId, new NewsEntry(now, result.Report.Lines));
        await AddNewsAsync(session, targetOwner!, new NewsEntry(now, result.TargetLines, Hostile: true));
        return await SaveAsync(session, result.Report);
    }

    private static async Task AddNewsAsync(IAsyncDocumentSession session, string ownerId, NewsEntry entry)
    {
        var newsKey = RavenSetup.NewsKey(ownerId);
        var news = await session.LoadAsync<NewsLog>(newsKey);
        if (news is null)
        {
            news = new NewsLog();
            await session.StoreAsync(news, newsKey);
        }

        news.Add(entry, NewsKept);
    }

    private static async Task<ActionReport> SaveAsync(IAsyncDocumentSession session, ActionReport report)
    {
        try
        {
            await session.SaveChangesAsync(); // mages and news in one transaction
        }
        catch (ConcurrencyException)
        {
            return ActionReport.Fail("Your mage was busy with another action. Nothing was spent, please try again.");
        }

        return report;
    }
}
