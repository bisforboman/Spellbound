using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>
/// Thin glue between pages and the domain: load the current round and the mage, run one domain action, save.
/// Each call is one database transaction (a Blazor Server circuit outlives one action). An action locks its mage's row
/// (an attack locks both mages, in owner order), so a second tap waits for the first and then acts on what it left:
/// no turn is spent twice. Mages and news are stored per game, and each Realm round is a game, so a new round starts everyone
/// over.
/// </summary>
/// <summary>The Realm round being played, with the rules it runs under.</summary>
public sealed record PlayedGame(long Id, Round Round, GameRules Rules);

public sealed class GameService(HostDatabase database, TimeProvider time, GameRules defaults, IRandomSource random)
{
    public const int NewsKept = 50;

    /// <summary>How many of the latest hostile acts the Chronicle page shows.</summary>
    public const int ChronicleShown = 50;

    private static readonly ActionReport Banned = ActionReport.Fail("Your account is banned.");

    private static readonly ActionReport Busy =
        ActionReport.Fail("Your mage was busy with another action. Nothing was spent, please try again.");

    /// <summary>
    /// The time actions run at, to whole microseconds: PostgreSQL stores no finer, so the action log's time is exactly
    /// the time the domain saw, and a replay gets the same result.
    /// </summary>
    public DateTimeOffset Now => time.GetUtcNow() is var now ? now.AddTicks(-(now.Ticks % 10)) : default;

    /// <summary>The current round, or null before the first one has started.</summary>
    public async Task<Round?> GetRoundAsync() => (await GetRealmAsync())?.Round;

    /// <summary>The current round with its rules, or null before the first one has started.</summary>
    public async Task<PlayedGame?> GetRealmAsync()
    {
        await using var db = await database.BeginAsync();
        return await LoadRealmAsync(db) is { } game ? new PlayedGame(game.Id, game.Round, game.RulesOf(defaults)) : null;
    }

    /// <summary>Read-only copy of the player's mage in the current round, with turns accrued to now (not saved).</summary>
    public async Task<Mage?> GetMageAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        if (await LoadRealmAsync(db) is not { } game)
        {
            return null;
        }

        var mage = await db.LoadMageAsync(game.Id, ownerId);
        mage?.AccrueTurns(Now, game.RulesOf(defaults));
        return mage;
    }

    public async Task<IReadOnlyList<NewsEntry>> GetNewsAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        return await LoadRealmAsync(db) is { } game ? (await db.LoadNewsAsync(game.Id, ownerId)).Entries : [];
    }

    /// <summary>The current round's Chronicle, newest first; empty before the first round.</summary>
    public async Task<IReadOnlyList<ChronicleEntry>> GetChronicleAsync()
    {
        await using var db = await database.BeginAsync();
        return await LoadRealmAsync(db) is { } game ? await db.LoadChronicleAsync(game.Id, ChronicleShown) : [];
    }

    /// <summary>How many hostile news entries arrived since the player last looked, and resets the count.</summary>
    public async Task<int> TakeUnseenAlertsAsync(string ownerId)
    {
        try
        {
            await using var db = await database.BeginAsync();
            if (await LoadRealmAsync(db) is not { } game)
            {
                return 0;
            }

            // Locks the row while reading, so an attack landing at the same moment waits and counts next time.
            var unseen = await db.ScalarAsync<int>(
                "SELECT unseen FROM news WHERE game_id = $1 AND owner_id = $2 FOR UPDATE", game.Id, ownerId);
            if (unseen > 0)
            {
                await db.ExecuteAsync("UPDATE news SET unseen = 0 WHERE game_id = $1 AND owner_id = $2", game.Id, ownerId);
                await db.CommitAsync();
            }

            return unseen;
        }
        catch (Exception ex) when (HostStore.IsBusy(ex))
        {
            return 0; // the news is busy being written; the alerts show next time
        }
    }

    /// <summary>Every mage in the current round, for rankings and picking targets. Fine for one server's players.</summary>
    public async Task<IReadOnlyList<Mage>> GetAllMagesAsync()
    {
        await using var db = await database.BeginAsync();
        if (await LoadRealmAsync(db) is not { } game)
        {
            return [];
        }

        var bans = await db.LoadBansAsync();
        return [.. (await db.LoadGameMagesAsync(game.Id)).Where(m => !bans.IsBanned(m.OwnerId))];
    }

    /// <summary>Another mage in the current round by its public id (never the owner id, which is an account key).</summary>
    public async Task<Mage?> GetRivalAsync(Guid mageId)
    {
        await using var db = await database.BeginAsync();
        if (await LoadRealmAsync(db) is not { } game)
        {
            return null;
        }

        var mage = await db.LoadMageByIdAsync(game.Id, mageId);
        return mage is not null && !await db.IsBannedAsync(mage.OwnerId) ? mage : null;
    }

    /// <summary>
    /// A mage's latest logged actions this round (and attacks and hexes against them), newest first, found by the mage's
    /// name. For the admin page: settling disputes and spotting abuse.
    /// </summary>
    public async Task<(Mage? Mage, IReadOnlyList<LoggedAction> Actions)> GetActionLogAsync(string mageName, int limit = 50)
    {
        await using var db = await database.BeginAsync();
        if (await LoadRealmAsync(db) is not { } game)
        {
            return (null, []);
        }

        var mage = (await db.LoadGameMagesAsync(game.Id))
            .FirstOrDefault(m => string.Equals(m.Name, mageName.Trim(), StringComparison.OrdinalIgnoreCase));
        return mage is null ? (null, []) : (mage, await db.LoadAsync(game.Id, mage.OwnerId, limit));
    }

    /// <summary>Every finished round's hall of fame, newest first.</summary>
    public async Task<IReadOnlyList<HallOfFame>> GetHallOfFameAsync()
    {
        await using var db = await database.BeginAsync();
        return await db.QueryAsync(
            "SELECT data FROM hall_of_fame ORDER BY game_id DESC", r => DomainJson.Deserialize<HallOfFame>(r.GetString(0)));
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
        if (ClosedReason(await LoadRealmAsync(db), now, out var game) is { } closed)
        {
            return closed;
        }

        if (await db.IsBannedAsync(ownerId))
        {
            return Banned;
        }

        var rules = game.RulesOf(defaults);
        var mage = Mage.Create(ownerId, trimmed, school, now, rules);
        if (!await db.InsertMageAsync(game.Id, mage)) // the key is (game, owner): one mage per round
        {
            return ActionReport.Fail("You already have a mage.");
        }

        var report = new ActionReport(true, 0, [$"{mage.Name} of {mage.School.DisplayName()} awakens."]);
        await db.WriteAsync(game.Id, mage, now, "create", new { name = trimmed, school }, [], report, rules);
        await db.CommitAsync();
        return report;
    }

    public Task<ActionReport> ExploreAsync(string ownerId, int turns) =>
        ActAsync(ownerId, "explore", async (db, game, now, rules) =>
        {
            // The phase and the median are inputs the mage's own state can't reproduce, so they go in the log.
            var conditions = await ExploreConditionsAsync(db, game, now, rules);
            return (new { turns, phase = conditions.Phase, medianLand = conditions.MedianLand },
                mage => mage.Explore(turns, now, rules, conditions));
        });

    /// <summary>What exploring depends on right now (the round's phase and the median land), for the Explore page's
    /// numbers and ledger; null before the first round.</summary>
    public async Task<ExploreConditions?> GetExploreConditionsAsync()
    {
        await using var db = await database.BeginAsync();
        return await LoadRealmAsync(db) is { } game ? await ExploreConditionsAsync(db, game, Now, game.RulesOf(defaults)) : null;
    }

    /// <summary>Exploring's conditions in a game: its phase now, and the median of its mages' land, banned ones left out
    /// as on Rivals.</summary>
    private static async Task<ExploreConditions> ExploreConditionsAsync(DbSession db, HostedGame game, DateTimeOffset now, GameRules rules)
    {
        var bans = await db.LoadBansAsync();
        var mages = (await db.LoadGameMagesAsync(game.Id)).Where(m => !bans.IsBanned(m.OwnerId));
        return ExploreConditions.At(game.Round, now, rules, mages);
    }

    public Task<ActionReport> BuildAsync(string ownerId, BuildingType type, int count) =>
        ActAsync(ownerId, "build", new { type, count }, (mage, now, rules) => mage.Build(type, count, now, rules));

    public Task<ActionReport> MeditateAsync(string ownerId, int turns) =>
        ActAsync(ownerId, "meditate", new { turns }, (mage, now, rules) => mage.Meditate(turns, now, rules));

    public Task<ActionReport> CastAsync(string ownerId, string spellId) =>
        ActAsync(ownerId, "cast", new { spellId }, (mage, now, rules) => mage.Cast(spellId, now, rules));

    public Task<ActionReport> RecruitAsync(string ownerId, string unitId, int count) =>
        ActAsync(ownerId, "recruit", new { unitId, count }, (mage, now, rules) => mage.Recruit(unitId, count, now, rules));

    public Task<ActionReport> AttackAsync(string ownerId, Guid targetId) =>
        HostileAsync(ownerId, targetId, "attack", new { targetMageId = targetId }, (mage, target, now, rules, luck) => mage.Attack(target, now, rules, luck));

    public Task<ActionReport> HexAsync(string ownerId, string hexId, Guid targetId) =>
        HostileAsync(ownerId, targetId, "hex", new { hexId, targetMageId = targetId }, (mage, target, now, rules, luck) => mage.CastHex(hexId, target, now, rules, luck));

    private Task<ActionReport> ActAsync(string ownerId, string name, object inputs, Func<Mage, DateTimeOffset, GameRules, ActionReport> action) =>
        ActAsync(ownerId, name, (_, _, now, rules) =>
            Task.FromResult<(object, Func<Mage, ActionReport>)>((inputs, mage => action(mage, now, rules))));

    /// <summary>
    /// Runs one action on the player's locked mage and saves it with its news and log row. <paramref name="prepare"/>
    /// reads whatever else the action needs in the same transaction and returns the inputs to log and the action.
    /// </summary>
    private Task<ActionReport> ActAsync(
        string ownerId, string name, Func<DbSession, HostedGame, DateTimeOffset, GameRules, Task<(object Inputs, Func<Mage, ActionReport> Act)>> prepare) =>
        LockedAsync(async db =>
        {
            var now = Now;
            if (ClosedReason(await LoadRealmAsync(db), now, out var game) is { } closed)
            {
                return closed;
            }

            if (await db.LoadMageAsync(game.Id, ownerId, forUpdate: true) is not { } mage)
            {
                return ActionReport.Fail("You don't have a mage yet.");
            }

            if (await db.IsBannedAsync(ownerId))
            {
                return Banned;
            }

            var rules = game.RulesOf(defaults);
            var (inputs, act) = await prepare(db, game, now, rules);
            var report = act(mage);
            if (!report.Succeeded)
            {
                return report;
            }

            await db.SaveMageAsync(game.Id, mage);
            await db.AddNewsAsync(game.Id, ownerId, new NewsEntry(now, report.Lines), NewsKept);
            await db.WriteAsync(game.Id, mage, now, name, inputs, [], report, rules);
            await db.CommitAsync(); // mage, news and the log row in one transaction
            return report;
        });

    /// <summary>
    /// Like <see cref="ActAsync"/>, but changes two mages. Both rows are locked in owner order before either is read,
    /// and both news rows likewise, so attacks between the same mages wait for each other instead of deadlocking.
    /// </summary>
    private Task<ActionReport> HostileAsync(
        string ownerId, Guid targetId, string name, object inputs, Func<Mage, Mage, DateTimeOffset, GameRules, IRandomSource, HostileReport> action) =>
        LockedAsync(async db =>
        {
            var now = Now;
            if (ClosedReason(await LoadRealmAsync(db), now, out var game) is { } closed)
            {
                return closed;
            }

            var locked = await db.LockMagesAsync(game.Id, ownerId, targetId);
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

            var luck = new RecordingRandom(random); // the draws go in the log, so the fight can be replayed
            var rules = game.RulesOf(defaults);
            var result = action(mage, target, now, rules, luck);
            if (!result.Report.Succeeded)
            {
                return result.Report;
            }

            await db.SaveMageAsync(game.Id, mage);
            if (target != mage)
            {
                await db.SaveMageAsync(game.Id, target);
            }

            var news = new[]
            {
                (Owner: ownerId, Entry: new NewsEntry(now, result.Report.Lines)),
                (Owner: target.OwnerId, Entry: new NewsEntry(now, result.TargetLines, Hostile: true)),
            };
            foreach (var (owner, entry) in news.OrderBy(n => n.Owner, StringComparer.Ordinal))
            {
                await db.AddNewsAsync(game.Id, owner, entry, NewsKept);
            }

            await db.WriteAsync(
                game.Id, mage, now, name, inputs, luck.Draws, result.Report, rules, target, result.TargetLines, result.Chronicle);
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

    /// <summary>The Realm round being played: the game the server always keeps running.</summary>
    private static Task<HostedGame?> LoadRealmAsync(DbSession db) => db.LoadRunningGameAsync(SpellboundHost.RealmMode);

    /// <summary>Why nobody can act right now (no round yet, or the round is over and waiting for its reset).</summary>
    private static ActionReport? ClosedReason(HostedGame? current, DateTimeOffset now, out HostedGame game)
    {
        game = current!;
        return current is null ? ActionReport.Fail("The first round hasn't started yet. Try again in a minute.")
            : current.Round.IsOpen(now) ? null
            : ActionReport.Fail($"Round {current.Round.Number} is over. The next round starts in a moment.");
    }
}
