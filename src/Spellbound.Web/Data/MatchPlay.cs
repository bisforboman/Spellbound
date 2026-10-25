using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// A player's side of a match (docs/roadmap.md, M2): each person commits their pick or orders for the step and waits for
/// the others; when everyone has, the bots choose theirs with the step's randomness and the step resolves
/// (<see cref="MatchStore.CommitAsync"/>). With one person, a commitment resolves the step at once.
/// </summary>
public sealed class MatchPlay(MatchStore store, TimeProvider time)
{
    /// <summary>Starts a match with the player in the first seat and bots in the rest. Returns its id.</summary>
    public Task<long> StartAsync(string ownerId, int players, GameRules rules)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, rules.MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, rules.MaxPlayers);
        return store.CreateAsync([ownerId, .. Enumerable.Repeat<string?>(null, players - 1)], rules, time.GetUtcNow());
    }

    /// <summary>A match the player sits in, or null (no such match, or not theirs to see).</summary>
    public async Task<LoadedMatch?> ViewAsync(long id, string ownerId) =>
        await store.LoadAsync(id) is { } loaded && loaded.SeatOf(ownerId) is not null ? loaded : null;

    /// <summary>The player drafts one of the traits they were offered. A pick doesn't wait for anyone, and it is the
    /// player's alone: it isn't the match's last report.</summary>
    public async Task<Report> TraitAsync(long id, string ownerId, Trait trait) =>
        await store.ApplyAsync(id, time.GetUtcNow(), (loaded, _) => loaded.SeatOf(ownerId) is { } seat && !loaded.Away.Contains(seat)
            ? loaded.Match.ChooseTrait(seat, trait)
            : Report.Fail("You don't sit in this match."), isStep: false) ?? Report.Fail("There is no such match.");

    /// <summary>The player picks a tile in the opening draft, on their turn; the bots after them pick theirs at once
    /// (<see cref="MatchStore.CommitAsync"/>).</summary>
    public Task<Report> PickAsync(long id, string ownerId, Hex tile) =>
        CommitAsync(id, ownerId, new Pick(0, tile), (match, seat, pick) => match.CheckPick(pick with { Seat = seat }));

    /// <summary>The player commits their orders for the round. When every person in the match has, the round resolves
    /// with the bots' orders.</summary>
    public Task<Report> OrdersAsync(long id, string ownerId, Orders orders) =>
        CommitAsync(id, ownerId, orders, (match, seat, mine) => match.Check(mine with { Seat = seat }));

    /// <summary>The player takes back what they committed this step, if it hasn't resolved yet.</summary>
    public Task<bool> WithdrawAsync(long id, string ownerId) => store.WithdrawAsync(id, ownerId);

    /// <summary>The match's host hands another person's seat to a bot.</summary>
    public Task<Report> HandToBotAsync(long id, string ownerId, int seat) => store.HandToBotAsync(id, ownerId, seat, time.GetUtcNow());

    private async Task<Report> CommitAsync<T>(long id, string ownerId, T mine, Func<Match, int, T, Report> check) =>
        await store.CommitAsync(id, ownerId, time.GetUtcNow(), MatchJson.Serialize(mine), (loaded, seat) => check(loaded.Match, seat, mine))
        is { } done ? done.Report : Report.Fail("There is no such match.");
}
