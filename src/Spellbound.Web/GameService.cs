using Microsoft.EntityFrameworkCore;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web;

/// <summary>
/// Thin glue between pages and the domain: load the mage, run one domain action, save.
/// Uses a fresh DbContext per call, because a Blazor Server circuit outlives a single action.
/// </summary>
public sealed class GameService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider time, GameRules rules)
{
    public const int NewsKept = 50;

    public GameRules Rules => rules;

    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>Read-only copy of the player's mage with turns accrued to now (not saved).</summary>
    public async Task<Mage?> GetMageAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var mage = await db.Mages.AsNoTracking().SingleOrDefaultAsync(m => m.OwnerId == ownerId);
        mage?.AccrueTurns(Now, rules);
        return mage;
    }

    public async Task<IReadOnlyList<NewsEntry>> GetNewsAsync(Guid mageId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.News.AsNoTracking()
            .Where(n => n.MageId == mageId)
            .OrderByDescending(n => n.Id)
            .Take(NewsKept)
            .ToListAsync();
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

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Mages.AnyAsync(m => m.OwnerId == ownerId))
        {
            return ActionReport.Fail("You already have a mage.");
        }

        var mage = Mage.Create(ownerId, trimmed, school, Now, rules);
        db.Mages.Add(mage);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return ActionReport.Fail("You already have a mage."); // lost a race on the unique OwnerId index
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

    private async Task<ActionReport> ActAsync(string ownerId, Func<Mage, DateTimeOffset, ActionReport> action)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var mage = await db.Mages.SingleOrDefaultAsync(m => m.OwnerId == ownerId);
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

        db.News.Add(new NewsEntry { MageId = mage.Id, At = now, Text = string.Join('\n', report.Lines) });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActionReport.Fail("Your mage was busy with another action. Nothing was spent, please try again.");
        }

        await db.News
            .Where(n => n.MageId == mage.Id)
            .OrderByDescending(n => n.Id)
            .Skip(NewsKept)
            .ExecuteDeleteAsync();

        return report;
    }
}
