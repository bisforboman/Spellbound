using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>
/// Thin glue between pages and the domain: load the current round and the mage, run one domain action, save.
/// Each call is one database transaction (a Blazor Server circuit outlives one action). An action locks its mage's row
/// (an attack locks both mages, in owner order), so a second tap waits for the first and then acts on what it left:
/// no turn is spent twice. Mages and news are stored per round, so a new round starts everyone over.
/// </summary>
public sealed class GameService(HostDatabase database, TimeProvider time, GameRules rules, IRandomSource random)
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
        await using var db = await database.BeginAsync();
        return await db.LoadCurrentRoundAsync();
    }

    /// <summary>Read-only copy of the player's mage in the current round, with turns accrued to now (not saved).</summary>
    public async Task<Mage?> GetMageAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        if (await db.LoadCurrentRoundAsync() is not { } round)
        {
            return null;
        }

        var mage = await db.LoadMageAsync(round.Number, ownerId);
        mage?.AccrueTurns(Now, rules);
        return mage;
    }

    public async Task<IReadOnlyList<NewsEntry>> GetNewsAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        return await db.LoadCurrentRoundAsync() is { } round ? (await db.LoadNewsAsync(round.Number, ownerId)).Entries : [];
    }

    /// <summary>How many hostile news entries arrived since the player last looked, and resets the count.</summary>
    public async Task<int> TakeUnseenAlertsAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        if (await db.LoadCurrentRoundAsync() is not { } round)
        {
            return 0;
        }

        // Locks the row while reading, so an attack landing at the same moment waits and counts next time.
        var unseen = await db.ScalarAsync<int>(
            "SELECT unseen FROM news WHERE round = $1 AND owner_id = $2 FOR UPDATE", round.Number, ownerId);
        if (unseen > 0)
        {
            await db.ExecuteAsync("UPDATE news SET unseen = 0 WHERE round = $1 AND owner_id = $2", round.Number, ownerId);
            await db.CommitAsync();
        }

        return unseen;
    }

    /// <summary>Every mage in the current round, for rankings and picking targets. Fine for one server's players.</summary>
    public async Task<IReadOnlyList<Mage>> GetAllMagesAsync()
    {
        await using var db = await database.BeginAsync();
        if (await db.LoadCurrentRoundAsync() is not { } round)
        {
            return [];
        }

        var bans = await db.LoadBansAsync();
        return [.. (await db.LoadRoundMagesAsync(round.Number)).Where(m => !bans.IsBanned(m.OwnerId))];
    }

    /// <summary>Another mage in the current round by its public id (never the owner id, which is an account key).</summary>
    public async Task<Mage?> GetRivalAsync(Guid mageId)
    {
        await using var db = await database.BeginAsync();
        if (await db.LoadCurrentRoundAsync() is not { } round)
        {
            return null;
        }

        var mage = await db.LoadMageByIdAsync(round.Number, mageId);
        return mage is not null && !await db.IsBannedAsync(mage.OwnerId) ? mage : null;
    }

    /// <summary>Every finished round's hall of fame, newest first.</summary>
    public async Task<IReadOnlyList<HallOfFame>> GetHallOfFameAsync()
    {
        await using var db = await database.BeginAsync();
        return await db.QueryAsync(
            "SELECT data FROM hall_of_fame ORDER BY round DESC", r => DomainJson.Deserialize<HallOfFame>(r.GetString(0)));
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

        await using var db = await database.BeginAsync();
        var now = Now;
        if (ClosedReason(await db.LoadCurrentRoundAsync(), now, out var round) is { } closed)
        {
            return closed;
        }

        if (await db.IsBannedAsync(ownerId))
        {
            return Banned;
        }

        var mage = Mage.Create(ownerId, trimmed, school, now, rules);
        if (!await db.InsertMageAsync(round.Number, mage)) // the key is (round, owner): one mage per round
        {
            return ActionReport.Fail("You already have a mage.");
        }

        await db.CommitAsync();
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

    private Task<ActionReport> ActAsync(string ownerId, Func<Mage, DateTimeOffset, ActionReport> action) =>
        LockedAsync(async db =>
        {
            var now = Now;
            if (ClosedReason(await db.LoadCurrentRoundAsync(), now, out var round) is { } closed)
            {
                return closed;
            }

            if (await db.LoadMageAsync(round.Number, ownerId, forUpdate: true) is not { } mage)
            {
                return ActionReport.Fail("You don't have a mage yet.");
            }

            if (await db.IsBannedAsync(ownerId))
            {
                return Banned;
            }

            var report = action(mage, now);
            if (!report.Succeeded)
            {
                return report;
            }

            await db.SaveMageAsync(round.Number, mage);
            await db.AddNewsAsync(round.Number, ownerId, new NewsEntry(now, report.Lines), NewsKept);
            await db.CommitAsync(); // mage and news in one transaction
            return report;
        });

    /// <summary>
    /// Like <see cref="ActAsync"/>, but changes two mages. Both rows are locked in owner order before either is read,
    /// and both news rows likewise, so attacks between the same mages wait for each other instead of deadlocking.
    /// </summary>
    private Task<ActionReport> HostileAsync(
        string ownerId, Guid targetId, Func<Mage, Mage, DateTimeOffset, HostileReport> action) =>
        LockedAsync(async db =>
        {
            var now = Now;
            if (ClosedReason(await db.LoadCurrentRoundAsync(), now, out var round) is { } closed)
            {
                return closed;
            }

            var locked = await db.LockMagesAsync(round.Number, ownerId, targetId);
            if (locked.Find(m => m.OwnerId == ownerId) is not { } mage)
            {
                return ActionReport.Fail("You don't have a mage yet.");
            }

            var bans = await db.LoadBansAsync();
            if (bans.IsBanned(ownerId))
            {
                return Banned;
            }

            if (locked.Find(m => m.Id == targetId) is not { } target || bans.IsBanned(target.OwnerId))
            {
                return ActionReport.Fail("There is no such mage this round.");
            }

            var result = action(mage, target, now);
            if (!result.Report.Succeeded)
            {
                return result.Report;
            }

            await db.SaveMageAsync(round.Number, mage);
            if (target != mage)
            {
                await db.SaveMageAsync(round.Number, target);
            }

            var news = new[]
            {
                (Owner: ownerId, Entry: new NewsEntry(now, result.Report.Lines)),
                (Owner: target.OwnerId, Entry: new NewsEntry(now, result.TargetLines, Hostile: true)),
            };
            foreach (var (owner, entry) in news.OrderBy(n => n.Owner, StringComparer.Ordinal))
            {
                await db.AddNewsAsync(round.Number, owner, entry, NewsKept);
            }

            await db.CommitAsync();
            return result.Report;
        });

    /// <summary>Runs an action in its own transaction. A row that stays locked too long, or a deadlock, is "busy".</summary>
    private async Task<ActionReport> LockedAsync(Func<DbSession, Task<ActionReport>> action)
    {
        try
        {
            await using var db = await database.BeginAsync();
            return await action(db);
        }
        catch (Exception ex) when (HostStore.IsBusy(ex))
        {
            return Busy;
        }
    }

    /// <summary>Why nobody can act right now (no round yet, or the round is over and waiting for its reset).</summary>
    private static ActionReport? ClosedReason(Round? current, DateTimeOffset now, out Round round)
    {
        round = current!;
        return current is null ? ActionReport.Fail("The first round hasn't started yet. Try again in a minute.")
            : current.IsOpen(now) ? null
            : ActionReport.Fail($"Round {current.Number} is over. The next round starts in a moment.");
    }
}
