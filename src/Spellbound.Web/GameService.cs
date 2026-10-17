using Raven.Client.Documents;
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
public sealed class GameService(IDocumentStore store, TimeProvider time, GameRules rules)
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

        var newsKey = RavenSetup.NewsKey(ownerId);
        var news = await session.LoadAsync<NewsLog>(newsKey);
        if (news is null)
        {
            news = new NewsLog();
            await session.StoreAsync(news, newsKey);
        }

        news.Add(new NewsEntry(now, report.Lines), NewsKept);
        try
        {
            await session.SaveChangesAsync(); // mage and news in one transaction
        }
        catch (ConcurrencyException)
        {
            return ActionReport.Fail("Your mage was busy with another action. Nothing was spent, please try again.");
        }

        return report;
    }
}
