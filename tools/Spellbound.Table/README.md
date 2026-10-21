# Spellbound.Table: AI players

A match on disk that AI players play from the command line, one seat each, against the scripted bots in the other
seats. The AI players are Claude Code agents (in a Claude Code session, so they run on the owner's subscription; no
API key), each with a personality from `personalities.md`. They learn the game only from `rules` and `view`, as a new
player would, and end each match with a short note on what felt strong, pointless or confusing.

It is a playtest tool, not a balance tool: a handful of matches is noise next to the simulator's thousand. Read the
notes and the refusals, not the win rates.

```bash
T="dotnet run --no-build --project tools/Spellbound.Table --"
dotnet build tools/Spellbound.Table
$T new  m.json --players 3 --ai 1,2 --seed 7   # seats 1 and 2 are AI; seat 3 is a scripted bot
$T rules                                         # the rules in brief and the action format
$T view  m.json --seat 1                         # what seat 1 can see, and what it should do next
$T check m.json --seat 1 '{"stance":"attune","target":"0,1"}'   # cost and result; changes nothing
$T act   m.json --seat 1 '{"stance":"attune","target":"0,1"}'   # commits, then waits for the other AI seats
```

- A step (an opening auction or a round) resolves once every AI seat has acted; the bots act then, and the step's
  randomness comes from the seed and the step, so a match replays.
- `act` waits (up to `--wait` seconds, 1200 by default) for the other AI seats, then prints what happened.
- Everything is logged to `m.json.log.jsonl`: each action, whether it was refused and why, and each step's report.
- A trait pick doesn't wait. A refused action changes nothing and says why.

## Running a session of AI matches

In Claude Code: build the tool, create one table per match, then start one background agent per AI seat with the
prompt below and its personality. When they are done, collect their notes and the logs into a report in
`docs/playtests/`.

> You are playing Spellbound, a strategy game, as Seat {N} at the table `{file}`, with this personality:
> {personality}. Learn the game only from `{T} rules` and `{T} view {file} --seat {N}`; don't read the repository's
> docs or code. Each step: `view`, think about what your personality would do, optionally `check` an action, then
> `act`. Play until `view` says the match is over. Then write a short note (under 200 words) to `{notes}`: what you
> tried, what felt strong, pointless or confusing, and any rule you misunderstood. Say "Seat {N}" when you mean
> yourself.
