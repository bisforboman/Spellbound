# Playing with friends: M2 decisions (22 Oct 2026)

M1 was a match against bots: bots commit at once, so every step resolved the moment the one person acted. M2 lets
several people share a match. The owner made these choices; each one was Claude's recommendation unless noted.

## Decided

- **Commit-and-wait.** A step (an opening Unveil's bids, or a round's orders) resolves only when every person in the
  match has committed. Bots still choose at the moment it resolves. Until then a person can **take back** their
  orders and commit again. Everyone sees *who* has committed, never *what*.
- **Joining: an invite link.** The host makes a match and shares a link; whoever opens it takes a free seat. Bots
  fill the seats nobody took when the host starts.
- **Deadline: 24 hours, then hold.** A person who hasn't committed 24 hours after a step began *holds*: their units
  stay put and they pick no stance (in the opening, they pass). It is checked by `GameKeeper`, the one global job,
  not a per-player timer.
- **Notifications, all four:** a round resolved; the others are waiting on you; you were attacked; an invited player
  joined.
- **A dropped player keeps watching.** After two missed deadlines in a row a bot plays the seat; the player can still
  open the match and see it.
- **The host can hand a seat to a bot**, by the same mechanism as the two-missed-deadlines rule.

## Built

- Commit-and-wait (22 Oct 2026). A person's commitment is stored on their own row of `match_players` (`pending`, and
  `pending_for`, the step it was for) and is never loaded with the match, so no page can show it to a rival. Only a
  step that resolves writes the match's last report, so picking a trait doesn't show anyone a report.

## Still to build, in order

1. The invite link and lobby.
2. The 24-hour deadline, holds, and a bot taking a dropped seat (and the host's hand-off).
3. The four notifications, over the web push already built (`Hosting/Push.cs`).
