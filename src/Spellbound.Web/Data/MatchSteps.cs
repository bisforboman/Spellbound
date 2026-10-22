using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// How a step resolves (docs/discussions/playing-with-friends.md): every person's commitment, a hold (a pass, in the
/// opening) for each person who let the deadline go by, and the bots' choices for the rest, with the step's randomness.
/// </summary>
public static class MatchSteps
{
    /// <param name="committed">Each committing seat's bid or orders, as stored (JSON).</param>
    /// <param name="holding">The seats of people who missed the deadline.</param>
    public static Report Resolve(Match match, IReadOnlyDictionary<int, string> committed, IReadOnlySet<int> holding, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(holding);
        if (match.Phase == MatchPhase.Opening)
        {
            // A seat with no trait yet gets one now, in secret: the bots', and a person's who let the deadline go by.
            foreach (var seat in match.Seats.Where(s => s.Trait is null && !committed.ContainsKey(s.Index)))
            {
                if (Bot.ChooseTrait(match, seat.Index, random) is { } trait)
                {
                    match.ChooseTrait(seat.Index, trait);
                }
            }

            return match.Auction(
                [.. match.Seats.Select(s =>
                    committed.TryGetValue(s.Index, out var bid) ? MatchJson.Deserialize<Bid>(bid) with { Seat = s.Index }
                    : holding.Contains(s.Index) ? new Bid(s.Index, null, 0)
                    : Bot.ChooseBid(match, s.Index, random))],
                random);
        }

        return match.Resolve(
            [.. match.Seats.Select(s =>
                committed.TryGetValue(s.Index, out var orders) ? MatchJson.Deserialize<Orders>(orders) with { Seat = s.Index }
                : holding.Contains(s.Index) ? Orders.Hold(s.Index)
                : Bot.ChooseOrders(match, s.Index, random))],
            random);
    }
}
