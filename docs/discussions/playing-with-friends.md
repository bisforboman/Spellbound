# Playing with friends: M2 decisions (22 Oct 2026)

M1 was a match against bots: bots commit at once, so every step resolved the moment the one person acted. M2 lets
several people share a match. The owner made these choices; each one was Claude's recommendation unless noted.

## Decided

- **Commit-and-wait.** A step (the bids on one opening tile, or a round's orders) resolves only when every person in
  it has committed: in the opening, only those still bidding in the Unveil (since 24 Oct 2026 its tiles go up one at
  a time, and a tile only bots still bid on goes at once). Bots still choose at the moment it resolves. Until then a person can **take back** their
  orders and commit again. Everyone sees *who* has committed, never *what*.
- **Joining: an invite link.** The host makes a match and shares a link; whoever opens it takes a free seat. Bots
  fill the seats nobody took when the host starts.
- **Deadline: 24 hours, then hold.** A person who hasn't committed 24 hours after a step began *holds*: their units
  stay put and they pick no stance (in the opening, they pass). *Since the stance draft (25 Oct 2026) every step is one
  person's turn: a missed pick takes Channel (or the first stance still free, aimed at nothing), and a missed turn on
  another player's stance passes. A match now has many more steps, so a slow match with friends can outlast the
  28 days a game may stay open (`MatchStore.OpenFor`).* It is checked by `GameKeeper`, the one global job,
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

- The invite link (22 Oct 2026). "Invite friends" on the new-match page opens an invite for the chosen size and
  shows its link (`/join/{code}`, 16 random characters). Whoever opens it, signed in, can take a free seat, one per
  account; the host starts when they like, and bots take the free seats. A host can have 3 invites open at once.
  Players are shown by seat ("the host", "joined", "you"), never by email.

- Deadlines and holds (22 Oct 2026). A step in a match with two or more people resolves 24 hours after it began,
  checked every minute by `GameKeeper`. A person who hasn't committed holds (passes in the opening; one who hadn't
  picked a trait gets one picked for them, as a bot would). Committing resets the count; two misses in a row and a
  bot plays the seat, and the person keeps watching. With one person left, the deadline goes: nobody waits on anyone.
  The page shows the time left and warns after one miss.
- The host's hand-off (22 Oct 2026): the invite's host can hand another person's seat to a bot, after a
  confirm step, but only a seat that hasn't committed this step (someone who has is still playing; Claude's choice,
  easy to widen). It isn't a miss. If everyone else has committed, the step resolves at once. If the host deletes
  their account, the invite goes with it and nobody can hand a seat over; the deadlines still apply.

- Notifications (22 Oct 2026, `Data/MatchNotices.cs`), sent once a change has committed and never to whoever caused
  it:
  - a step resolved: everyone else in the match, "Round 3 resolved", and what's next;
  - you were attacked: instead of the above, for a person whose land a rival attacked that round;
  - the others are waiting on you: when everyone but one person has committed, that person;
  - someone joined your invite: the host.

  Two more that the flow needed, Claude's addition, kept by the owner (22 Oct 2026): "your match has started", to the people who joined when the
  host starts it, and "a bot plays your seat now", to a person replaced after two misses or by the host. A person
  replaced by a bot hears nothing more until the match ends, which everyone with a seat hears.

  No reminder before the deadline for now (the owner, 22 Oct 2026): "waiting on you" reaches only the last one left;
  revisit after real play with friends.

M2 is built.
