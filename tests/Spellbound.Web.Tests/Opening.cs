using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>Getting a match through its opening in tests: the tiles go up one at a time, so how many steps it takes
/// depends on who wins what.</summary>
internal static class Opening
{
    /// <summary>Every one of these people passes on every tile they are still bidding on, until the rounds begin.</summary>
    public static async Task PassAsync(MatchPlay play, long id, params string[] owners)
    {
        for (var step = 0; step < 100; step++)
        {
            if ((await play.ViewAsync(id, owners[0]))!.Match.Phase != MatchPhase.Opening)
            {
                return;
            }

            foreach (var owner in owners)
            {
                var view = (await play.ViewAsync(id, owner))!;
                if (view.Match.Phase == MatchPhase.Opening && view.SeatOf(owner) is { } seat
                    && view.InStep.Contains(seat) && !view.Committed.Contains(seat))
                {
                    await play.BidAsync(id, owner, null, 0);
                }
            }
        }

        throw new InvalidOperationException("The opening didn't end.");
    }
}
