using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// How a step resolves (docs/discussions/playing-with-friends.md): a step is one seat's turn, an opening pick or a
/// turn in a round. The person's commitment, else for a person who let the deadline go by a bot's pick in the opening
/// and Channel or a pass in the rounds, else the bot's choice, with the step's randomness.
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
            // deadline go by. A seat that never picks before the rounds gets one as the opening ends.
            var picker = match.Picker!.Value;
            if (match.Seats[picker].Trait is null && !committed.ContainsKey(picker) && Bot.ChooseTrait(match, picker, random) is { } trait)
            {
                match.ChooseTrait(picker, trait);
            }

            var pick = committed.TryGetValue(picker, out var mine) ? MatchJson.Deserialize<Pick>(mine) with { Seat = picker }
                : Bot.ChoosePick(match, picker, random)!;
            return (match.Draft(pick, random), new HashSet<int>());
        }

        // A round's turn: the person's orders, else Channel or a pass for a person who let the deadline go by, else the bot's.
        var actor = match.Actor!.Value;
        var orders = committed.TryGetValue(actor, out var given) ? MatchJson.Deserialize<Orders>(given) with { Seat = actor }
            : holding.Contains(actor) ? match.Passing(actor)
            : Bot.ChooseOrders(match, actor, random);
        var attacked = orders is { Stance: Stance.Attack, Target: { } target } && match.OwnerOf(target) is { } owner && owner != actor
            ? new HashSet<int> { owner }
            : new HashSet<int>();
        return (match.Take(orders, random), attacked);
    }
}
