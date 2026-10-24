using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// How a step resolves (docs/discussions/playing-with-friends.md): every person's commitment, a hold for each person who
/// let the deadline go by (in the opening, a bot picks for them), and the bots' choices for the rest, with the step's
/// randomness.
/// </summary>
public static class MatchSteps
{
    /// <param name="committed">Each committing seat's pick or orders, as stored (JSON).</param>
    /// <param name="holding">The seats of people who missed the deadline.</param>
    /// <returns>What happened, and the seats whose land was attacked by someone else (for their notification).</returns>
    public static (Report Report, IReadOnlySet<int> Attacked) Resolve(Match match, IReadOnlyDictionary<int, string> committed, IReadOnlySet<int> holding, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(holding);
        if (match.Phase == MatchPhase.Opening)
        {
            // The picker gets a trait now, in secret, if it has none and didn't commit: a bot, or a person who let the
            // deadline go by. Everyone picks in the first draft, so every bot has one from then on.
            var picker = match.Picker!.Value;
            if (match.Seats[picker].Trait is null && !committed.ContainsKey(picker) && Bot.ChooseTrait(match, picker, random) is { } trait)
            {
                match.ChooseTrait(picker, trait);
            }

            var pick = committed.TryGetValue(picker, out var mine) ? MatchJson.Deserialize<Pick>(mine) with { Seat = picker }
                : Bot.ChoosePick(match, picker, random)!;
            return (match.Draft(pick, random), new HashSet<int>());
        }

        List<Orders> all = [.. match.Seats.Select(s =>
            committed.TryGetValue(s.Index, out var orders) ? MatchJson.Deserialize<Orders>(orders) with { Seat = s.Index }
            : holding.Contains(s.Index) ? Orders.Hold(s.Index)
            : Bot.ChooseOrders(match, s.Index, random))];
        var attacked = all
            .Where(o => o.Stance == Stance.Attack && o.Target is { } target && match.OwnerOf(target) is { } owner && owner != o.Seat)
            .Select(o => match.OwnerOf(o.Target!.Value)!.Value)
            .ToHashSet();
        return (match.Resolve(all, random), attacked);
    }
}
