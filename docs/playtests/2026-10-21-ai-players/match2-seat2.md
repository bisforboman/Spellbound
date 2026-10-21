# Match 2, Seat 2 (Rules-lawyer)

Final: Seat 2 23, Seat 1 22, Seat 3 16. Seat 2 won through Hearthkeeper (+3 for 5 Steady tiles).

What I tried: I bid all my mana (2) in auction 3 and won 3 Steady tiles, which maxed the trait early. Against the monster I used Lure and then a Golem (3 against 2). I moved units off a conquered tile in the same round and scorched before an attack.

Loopholes and oddities:
- Passing pays. Seat 1 passed all three auctions, got 3 free tiles and kept all 9 mana.
- `{"trait":..., "bid":...}` in one action silently drops the bid.
- The rules say traits are "hidden until the end", but they were shown when round 1 began.
- Scorch charges full price even when nothing burns: twice the units had moved or already burned (another seat's scorch landed first). I paid 4 and 6 mana for 0.
- Scorch 9 on 7 Imps plus 1 Golem: `check` said "burn 9 power", but only the 7 Imps burned and the Golem kept full power. Partial burns on a Golem do nothing, and you still pay for them.
- The attack preview ignores your own scorch. It said "keeping 3 Imps" even though the scorch would remove the defender.
- After a win, all the attackers move onto the taken tile, which left my home tile empty. That surprised me.

Strong: scorch before attacks. Pointless: ward on guesses.
