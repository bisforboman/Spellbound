using Microsoft.EntityFrameworkCore;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>
/// Thin glue between pages and the domain: load the current round and the mage, run one domain action, save.
/// Opens a database context per call, because a Blazor Server circuit outlives one action.
/// Mages and news are stored per round, so a new round starts everyone over.
/// </summary>
public sealed class GameService(IDbContextFactory<HostDb> dbs, TimeProvider time, GameRules rules, IRandomSource random)
{
    public const int NewsKept = 50;

    private static readonly ActionReport Banned = ActionReport.Fail("Your account is banned.");

    private static readonly ActionReport Busy =
        ActionReport.Fail("Your mage was busy with another action. Nothing was spent, please try again.");

    public GameRules Rules => rules;

    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>The current round, or null before the first one has started.</summary>
    public async Task<Round?> GetRoundAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        return (await db.LoadCurrentRoundAsync())?.Round;
    }

    /// <summary>Read-only copy of the player's mage in the current round, with turns accrued to now (not saved).</summary>
    public async Task<Mage?> GetMageAsync(string ownerId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        if ((await db.LoadCurrentRoundAsync())?.Round is not { } round)
        {
            return null;
        }

        var mage = (await db.Mages().AsNoTracking().SingleOrDefaultAsync(m => m.Round == round.Number && m.OwnerId == ownerId))?.Load();
        mage?.AccrueTurns(Now, rules);
        return mage;
    }

    public async Task<IReadOnlyList<NewsEntry>> GetNewsAsync(string ownerId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        if ((await db.LoadCurrentRoundAsync())?.Round is not { } round)
        {
            return [];
        }

        var news = await db.News().AsNoTracking().SingleOrDefaultAsync(n => n.Round == round.Number && n.OwnerId == ownerId);
        return news?.Load().Entries ?? [];
    }

    /// <summary>How many hostile news entries arrived since the player last looked, and resets the count.</summary>
    public async Task<int> TakeUnseenAlertsAsync(string ownerId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        if ((await db.LoadCurrentRoundAsync())?.Round is not { } round)
        {
            return 0;
        }

        var news = db.News().Where(n => n.Round == round.Number && n.OwnerId == ownerId);
        var unseen = await news.Select(n => n.Unseen).SingleOrDefaultAsync();
        if (unseen > 0)
        {
            // Subtract what was read rather than set 0, so an attack landing in between still counts next time.
            await news.ExecuteUpdateAsync(set => set.SetProperty(n => n.Unseen, n => n.Unseen - unseen));
        }

        return unseen;
    }

    /// <summary>Every mage in the current round, for rankings and picking targets. Fine for one server's players.</summary>
    public async Task<IReadOnlyList<Mage>> GetAllMagesAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        if ((await db.LoadCurrentRoundAsync())?.Round is not { } round)
        {
            return [];
        }

        var bans = await db.LoadBansAsync();
        return [.. (await db.LoadRoundMagesAsync(round.Number)).Where(m => !bans.IsBanned(m.OwnerId))];
    }

    /// <summary>Another mage in the current round by its public id (never the owner id, which is an account key).</summary>
    public async Task<Mage?> GetRivalAsync(Guid mageId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        if ((await db.LoadCurrentRoundAsync())?.Round is not { } round)
        {
            return null;
        }

        var record = await db.Mages().AsNoTracking().SingleOrDefaultAsync(m => m.Round == round.Number && m.MageId == mageId);
        return record is not null && !await db.Bans.AnyAsync(b => b.OwnerId == record.OwnerId) ? record.Load() : null;
    }

    /// <summary>Every finished round's hall of fame, newest first.</summary>
    public async Task<IReadOnlyList<HallOfFame>> GetHallOfFameAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        var all = await db.HallsOfFame().AsNoTracking().OrderByDescending(f => f.Round).Select(f => f.Data).ToListAsync();
        return [.. all.Select(DomainJson.Deserialize<HallOfFame>)];
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

        await using var db = await dbs.CreateDbContextAsync();
        var now = Now;
        if (ClosedReason((await db.LoadCurrentRoundAsync())?.Round, now, out var round) is { } closed)
        {
            return closed;
        }

        if (await db.Bans.AnyAsync(b => b.OwnerId == ownerId))
        {
            return Banned;
        }

        var mage = Mage.Create(ownerId, trimmed, school, now, rules);
        db.Mages().Add(MageRecord.For(round.Number, mage));
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (HostStore.IsDuplicateKey(ex)) // the key is (round, owner): one mage per round
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
        await using var db = await dbs.CreateDbContextAsync();
        var now = Now;
        if (ClosedReason((await db.LoadCurrentRoundAsync())?.Round, now, out var round) is { } closed)
        {
            return closed;
        }

        var record = await db.Mages().SingleOrDefaultAsync(m => m.Round == round.Number && m.OwnerId == ownerId);
        if (record is null)
        {
            return ActionReport.Fail("You don't have a mage yet.");
        }

        if (await db.Bans.AnyAsync(b => b.OwnerId == ownerId))
        {
            return Banned;
        }

        var mage = record.Load();
        var report = action(mage, now);
        if (!report.Succeeded)
        {
            return report;
        }

        record.Save(mage);
        await AddNewsAsync(db, round.Number, ownerId, new NewsEntry(now, report.Lines));
        return await SaveAsync(db, report);
    }

    /// <summary>
    /// Like <see cref="ActAsync"/>, but changes two mages. Both rows carry their version, so if either mage acts at
    /// the same moment, the save fails and nothing happens.
    /// </summary>
    private async Task<ActionReport> HostileAsync(
        string ownerId, Guid targetId, Func<Mage, Mage, DateTimeOffset, HostileReport> action)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var now = Now;
        if (ClosedReason((await db.LoadCurrentRoundAsync())?.Round, now, out var round) is { } closed)
        {
            return closed;
        }

        var record = await db.Mages().SingleOrDefaultAsync(m => m.Round == round.Number && m.OwnerId == ownerId);
        if (record is null)
        {
            return ActionReport.Fail("You don't have a mage yet.");
        }

        var bans = await db.LoadBansAsync();
        if (bans.IsBanned(ownerId))
        {
            return Banned;
        }

        var mage = record.Load();
        var targetRecord = record.MageId == targetId
            ? record
            : await db.Mages().SingleOrDefaultAsync(m => m.Round == round.Number && m.MageId == targetId);
        if (targetRecord is null || bans.IsBanned(targetRecord.OwnerId))
        {
            return ActionReport.Fail("There is no such mage this round.");
        }

        var target = targetRecord == record ? mage : targetRecord.Load();
        var result = action(mage, target, now);
        if (!result.Report.Succeeded)
        {
            return result.Report;
        }

        record.Save(mage);
        targetRecord.Save(target);
        await AddNewsAsync(db, round.Number, ownerId, new NewsEntry(now, result.Report.Lines));
        await AddNewsAsync(db, round.Number, targetRecord.OwnerId, new NewsEntry(now, result.TargetLines, Hostile: true));
        return await SaveAsync(db, result.Report);
    }

    /// <summary>Why nobody can act right now (no round yet, or the round is over and waiting for its reset).</summary>
    private static ActionReport? ClosedReason(Round? current, DateTimeOffset now, out Round round)
    {
        round = current!;
        return current is null ? ActionReport.Fail("The first round hasn't started yet. Try again in a minute.")
            : current.IsOpen(now) ? null
            : ActionReport.Fail($"Round {current.Number} is over. The next round starts in a moment.");
    }

    private static async Task AddNewsAsync(HostDb db, int round, string ownerId, NewsEntry entry)
    {
        var record = await db.News().SingleOrDefaultAsync(n => n.Round == round && n.OwnerId == ownerId);
        if (record is null)
        {
            record = db.News().Add(new NewsRecord { Round = round, OwnerId = ownerId }).Entity;
        }

        var log = record.Load();
        log.Add(entry, NewsKept);
        record.Save(log);
    }

    private static async Task<ActionReport> SaveAsync(HostDb db, ActionReport report)
    {
        try
        {
            await db.SaveChangesAsync(); // mages and news in one transaction
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || HostStore.IsDuplicateKey(ex))
        {
            return Busy;
        }

        return report;
    }
}
