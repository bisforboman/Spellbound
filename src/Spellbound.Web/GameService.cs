using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web;

/// <summary>
/// Thin glue between pages and the domain: load the current round and the mage, run one domain action, save.
/// Opens a session per call (a Blazor Server circuit outlives one action) and only loads by key,
/// because RavenDB loads are always consistent while queries may lag behind writes.
/// Mages and news live under the current round's keys, so a new round starts everyone over.
/// </summary>
public sealed class GameService(IDocumentStore store, TimeProvider time, GameRules rules, IRandomSource random)
{
    public const int NewsKept = 50;

    private static readonly ActionReport Banned = ActionReport.Fail("Your account is banned.");

    public GameRules Rules => rules;

    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>The current round, or null before the first one has started.</summary>
    public async Task<Round?> GetRoundAsync()
    {
        using var session = store.OpenAsyncSession();
        return await CurrentRoundAsync(session);
    }

    /// <summary>Read-only copy of the player's mage in the current round, with turns accrued to now (not saved).</summary>
    public async Task<Mage?> GetMageAsync(string ownerId)
    {
        using var session = store.OpenAsyncSession();
        if (await CurrentRoundAsync(session) is not { } round)
        {
            return null;
        }

        var mage = await session.LoadAsync<Mage>(RavenSetup.MageKey(round.Number, ownerId));
        mage?.AccrueTurns(Now, rules);
        return mage;
    }

    public async Task<IReadOnlyList<NewsEntry>> GetNewsAsync(string ownerId)
    {
        using var session = store.OpenAsyncSession();
        if (await CurrentRoundAsync(session) is not { } round)
        {
            return [];
        }

        var log = await session.LoadAsync<NewsLog>(RavenSetup.NewsKey(round.Number, ownerId));
        return log?.Entries ?? [];
    }

    /// <summary>How many hostile news entries arrived since the player last looked, and resets the count.</summary>
    public async Task<int> TakeUnseenAlertsAsync(string ownerId)
    {
        using var session = store.OpenAsyncSession();
        if (await CurrentRoundAsync(session) is not { } round)
        {
            return 0;
        }

        var key = RavenSetup.NewsKey(round.Number, ownerId);
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
    /// Every mage in the current round, for rankings and picking targets. Streamed by key prefix, so it needs no
    /// index and includes a mage created a moment ago. Fine for one server's players; page it if that grows.
    /// </summary>
    public async Task<IReadOnlyList<Mage>> GetAllMagesAsync()
    {
        using var session = store.OpenAsyncSession();
        if (await CurrentRoundAsync(session) is not { } round)
        {
            return [];
        }

        var bans = await RavenSetup.LoadBansAsync(session);
        return [.. (await RavenSetup.LoadRoundMagesAsync(session, round.Number)).Where(m => !bans.IsBanned(m.OwnerId))];
    }

    /// <summary>Another mage in the current round by its public id (never the owner id, which is an account key).</summary>
    public async Task<Mage?> GetRivalAsync(Guid mageId)
    {
        using var session = store.OpenAsyncSession();
        if (await CurrentRoundAsync(session) is not { } round)
        {
            return null;
        }

        var mage = await session.Query<Mage>().Where(m => m.Id == mageId).FirstOrDefaultAsync();
        return mage is not null
               && session.Advanced.GetDocumentId(mage).StartsWith(RavenSetup.MagePrefix(round.Number), StringComparison.Ordinal)
               && !(await RavenSetup.LoadBansAsync(session)).IsBanned(mage.OwnerId)
            ? mage
            : null;
    }

    /// <summary>Every finished round's hall of fame, newest first.</summary>
    public async Task<IReadOnlyList<HallOfFame>> GetHallOfFameAsync()
    {
        using var session = store.OpenAsyncSession();
        var all = new List<HallOfFame>();
        await using var stream = await session.Advanced.StreamAsync<HallOfFame>(startsWith: RavenSetup.HallOfFamePrefix);
        while (await stream.MoveNextAsync())
        {
            all.Add(stream.Current.Document);
        }

        return [.. all.OrderByDescending(f => f.RoundNumber)];
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

        using var session = store.OpenAsyncSession();
        var now = Now;
        if (ClosedReason(await CurrentRoundAsync(session), now, out var round) is { } closed)
        {
            return closed;
        }

        if ((await RavenSetup.LoadBansAsync(session)).IsBanned(ownerId))
        {
            return Banned;
        }

        var mage = Mage.Create(ownerId, trimmed, school, now, rules);
        // An empty change vector means the key must not exist yet: one mage per player per round.
        await session.StoreAsync(mage, changeVector: string.Empty, RavenSetup.MageKey(round.Number, ownerId));
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

        var now = Now;
        if (ClosedReason(await CurrentRoundAsync(session), now, out var round) is { } closed)
        {
            return closed;
        }

        var mage = await session.LoadAsync<Mage>(RavenSetup.MageKey(round.Number, ownerId));
        if (mage is null)
        {
            return ActionReport.Fail("You don't have a mage yet.");
        }

        var bans = await RavenSetup.LoadBansAsync(session);
        if (bans.IsBanned(ownerId))
        {
            return Banned;
        }

        var report = action(mage, now);
        if (!report.Succeeded)
        {
            return report;
        }

        await AddNewsAsync(session, round.Number, ownerId, new NewsEntry(now, report.Lines));
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

        var now = Now;
        if (ClosedReason(await CurrentRoundAsync(session), now, out var round) is { } closed)
        {
            return closed;
        }

        var mage = await session.LoadAsync<Mage>(RavenSetup.MageKey(round.Number, ownerId));
        if (mage is null)
        {
            return ActionReport.Fail("You don't have a mage yet.");
        }

        var bans = await RavenSetup.LoadBansAsync(session);
        if (bans.IsBanned(ownerId))
        {
            return Banned;
        }

        // The query only finds the target's owner; the mage itself is then loaded by key, which is always current.
        // A mage id from an earlier round finds that owner's current mage, which is a different mage: refuse it.
        var targetOwner = await session.Query<Mage>()
            .Where(m => m.Id == targetId)
            .Select(m => m.OwnerId)
            .FirstOrDefaultAsync();
        var target = targetOwner is null ? null
            : targetOwner == ownerId ? mage
            : await session.LoadAsync<Mage>(RavenSetup.MageKey(round.Number, targetOwner));
        if (target is null || target.Id != targetId || bans.IsBanned(target.OwnerId))
        {
            return ActionReport.Fail("There is no such mage this round.");
        }

        var result = action(mage, target, now);
        if (!result.Report.Succeeded)
        {
            return result.Report;
        }

        await AddNewsAsync(session, round.Number, ownerId, new NewsEntry(now, result.Report.Lines));
        await AddNewsAsync(session, round.Number, targetOwner!, new NewsEntry(now, result.TargetLines, Hostile: true));
        return await SaveAsync(session, result.Report);
    }

    private static async Task<Round?> CurrentRoundAsync(IAsyncDocumentSession session) =>
        (await session.LoadAsync<CurrentRound>(RavenSetup.CurrentRoundKey))?.Round;

    /// <summary>Why nobody can act right now (no round yet, or the round is over and waiting for its reset).</summary>
    private static ActionReport? ClosedReason(Round? current, DateTimeOffset now, out Round round)
    {
        round = current!;
        return current is null ? ActionReport.Fail("The first round hasn't started yet. Try again in a minute.")
            : current.IsOpen(now) ? null
            : ActionReport.Fail($"Round {current.Number} is over. The next round starts in a moment.");
    }

    private static async Task AddNewsAsync(IAsyncDocumentSession session, int round, string ownerId, NewsEntry entry)
    {
        var newsKey = RavenSetup.NewsKey(round, ownerId);
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
