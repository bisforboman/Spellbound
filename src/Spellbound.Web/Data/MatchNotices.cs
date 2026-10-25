using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>
/// The notifications a match sends (docs/discussions/playing-with-friends.md): a step resolved, the others are waiting
/// on you, you were attacked, someone joined your invite. Built inside a change and sent only once it has committed;
/// never to the person whose own action caused it, who is looking at the page.
/// </summary>
public static class MatchNotices
{
    /// <summary>The step a match is on, in words: "opening draft 2, pick 1", "round 3, pick 2" or "round 3, Summon".</summary>
    public static string StepName(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Phase == MatchPhase.Opening ? $"opening draft {match.OpeningUnveil}, pick {match.PickNumber}"
            : match.OpenStance is { } open ? $"round {match.CurrentRound}, {open}"
            : $"round {match.CurrentRound}, pick {match.Picks.Count + 1}";
    }

    /// <summary>Everyone else has committed and one person is left.</summary>
    public static PushMessage WaitingOnYou(long id, string ownerId, string step) =>
        new(ownerId, $"Match {id}: they're waiting on you", $"Everyone else has committed to {step}.", Url(id));

    /// <summary>
    /// After a step resolved: the person whose turn it is now hears it's their move (unless they are
    /// <paramref name="actor"/>), a person whose land was
    /// attacked hears that first, and a person a bot has just replaced hears that instead. At the end, everyone with a
    /// seat hears it, watchers too.
    /// </summary>
    /// <param name="loaded">The match as it was loaded (its seats and who was away); its match is the resolved one.</param>
    /// <param name="resolved">The step that resolved (<see cref="StepName"/>, from before it did).</param>
    /// <param name="nowAway">The seats a bot took over as this step resolved.</param>
    public static IEnumerable<PushMessage> Resolved(LoadedMatch loaded, string resolved, IReadOnlySet<int> attacked,
        IReadOnlySet<int> nowAway, int? actor)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var match = loaded.Match;
        var id = loaded.Id;
        var ended = match.Phase == MatchPhase.Ended;
        var next = ended ? "The match is over: see who won." : $"{MatchStore.Describe(match, true)}: your move.";
        for (var seat = 0; seat < loaded.Owners.Count; seat++)
        {
            if (loaded.Owners[seat] is not { } owner || seat == actor || (loaded.Away.Contains(seat) && !ended)
                || (!ended && !loaded.InStep.Contains(seat) && !nowAway.Contains(seat) && !attacked.Contains(seat)))
            {
                continue;
            }

            yield return nowAway.Contains(seat) && !ended
                ? new PushMessage(owner, $"Match {id}: a bot plays your seat now",
                    "You missed two deadlines in a row. You can still watch the match.", Url(id))
                : attacked.Contains(seat)
                    ? new PushMessage(owner, $"Match {id}: you were attacked", $"In {resolved}. {next}", Url(id))
                    : new PushMessage(owner, $"Match {id}: {Capitalised(resolved)} resolved",
                        next, Url(id));
        }
    }

    /// <summary>The host handed a person's seat to a bot.</summary>
    public static PushMessage HandedOver(long id, string ownerId) =>
        new(ownerId, $"Match {id}: a bot plays your seat now", "The host handed it to a bot. You can still watch the match.", Url(id));

    /// <summary>Someone took a seat in the host's invite.</summary>
    public static PushMessage Joined(Lobby lobby) =>
        new(lobby.HostId, "Someone joined your invite",
            $"{lobby.Members.Count} of {lobby.Seats} seats taken. Start the match when you're ready.", $"join/{lobby.Code}");

    /// <summary>The host started a match the person had joined.</summary>
    public static PushMessage Started(long id, string ownerId) =>
        new(ownerId, $"Match {id} has started", "Choose your trait, then draft your opening tiles.", Url(id));

    private static string Url(long id) => $"match/{id}";

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
