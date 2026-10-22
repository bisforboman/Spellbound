using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// A player's side of a match (docs/roadmap.md, M2): each person commits their bid or orders for the step and waits for
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
        await store.ApplyAsync(id, time.GetUtcNow(), (loaded, _) => loaded.SeatOf(ownerId) is { } seat
            ? loaded.Match.ChooseTrait(seat, trait)
            : Report.Fail("You don't sit in this match."), isStep: false) ?? Report.Fail("There is no such match.");

    /// <summary>The player commits a bid (no tile: a pass) for the current opening auction. When every person in the
    /// match has, the auction settles with the bots' bids; bots that haven't drafted a trait draft one first, in
    /// secret.</summary>
    public Task<Report> BidAsync(long id, string ownerId, Hex? tile, int amount) =>
        CommitAsync(id, ownerId, new Bid(0, tile, tile is null ? 0 : amount),
            (match, seat, bid) => match.CheckBid(bid with { Seat = seat }),
            (match, mine, random) =>
            {
                foreach (var bot in match.Seats.Where(s => !mine.ContainsKey(s.Index) && s.Trait is null))
                {
                    if (Bot.ChooseTrait(match, bot.Index, random) is { } trait)
                    {
                        match.ChooseTrait(bot.Index, trait);
                    }
                }

                return match.Auction(
                    [.. match.Seats.Select(s => mine.TryGetValue(s.Index, out var bid) ? bid with { Seat = s.Index } : Bot.ChooseBid(match, s.Index, random))],
                    random);
            });

    /// <summary>The player commits their orders for the round. When every person in the match has, the round resolves
    /// with the bots' orders.</summary>
    public Task<Report> OrdersAsync(long id, string ownerId, Orders orders) =>
        CommitAsync(id, ownerId, orders,
            (match, seat, mine) => match.Check(mine with { Seat = seat }),
            (match, mine, random) => match.Resolve(
                [.. match.Seats.Select(s => mine.TryGetValue(s.Index, out var o) ? o with { Seat = s.Index } : Bot.ChooseOrders(match, s.Index, random))],
                random));

    /// <summary>The player takes back what they committed this step, if it hasn't resolved yet.</summary>
    public Task<bool> WithdrawAsync(long id, string ownerId) => store.WithdrawAsync(id, ownerId);

    private async Task<Report> CommitAsync<T>(long id, string ownerId, T mine, Func<Match, int, T, Report> check,
        Func<Match, IReadOnlyDictionary<int, T>, IRandomSource, Report> resolve) =>
        await store.CommitAsync(id, ownerId, time.GetUtcNow(), MatchJson.Serialize(mine),
            (loaded, seat) => check(loaded.Match, seat, mine),
            (loaded, all, random) => resolve(loaded.Match, all.ToDictionary(p => p.Key, p => MatchJson.Deserialize<T>(p.Value)), random))
        is { } done ? done.Report : Report.Fail("There is no such match.");
}
