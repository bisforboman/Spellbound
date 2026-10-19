using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// A player's side of a match in M1 (docs/roadmap.md): one person against bots, played at once. Each action the player
/// takes is checked against their seat, the bots choose theirs with the same step's randomness, and the whole step
/// resolves in one <see cref="MatchStore.ApplyAsync"/>.
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

    /// <summary>The player's bid (no tile: a pass) with the bots' bids, settling the current opening auction.</summary>
    public Task<Report> BidAsync(long id, string ownerId, Hex? tile, int amount) =>
        ApplyAsync(id, ownerId, (match, seat, random) => match.Auction(
            [.. match.Seats.Select(s => s.Index == seat ? new Bid(seat, tile, tile is null ? 0 : amount) : Bot.ChooseBid(match, s.Index, random))],
            random));

    private async Task<Report> ApplyAsync(long id, string ownerId, Func<Match, int, IRandomSource, Report> action) =>
        await store.ApplyAsync(id, time.GetUtcNow(), (loaded, random) =>
        {
            if (loaded.SeatOf(ownerId) is not { } seat)
            {
                return Report.Fail("You don't sit in this match.");
            }

            // M1 is one person against bots: every other seat must be a bot, so nobody else's choice is overruled.
            if (loaded.Owners.Where((owner, i) => i != seat && owner is not null).Any())
            {
                return Report.Fail("This match has other players; it can't be played at once.");
            }

            return action(loaded.Match, seat, random);
        }) ?? Report.Fail("There is no such match.");
}
