# Spellbound — paper prototype (3 players)

A one-page version of `game-design.md` for a first test on a table. All numbers are first guesses; note what feels wrong.

**You need:** 19 paper hexes · coins for mana · cubes in 3 colours (Imps) and larger pieces in the same colours (Golems) · a few neutral cubes for monsters · paper slips for secret bids and stances · a d6 · a score track.

## Setup

1. Make 19 face-down hexes and lay them out as a round board (1 in the middle, a ring of 6, an outer ring of 12).
2. Write a type on each: **Steady** 7, **Shifting** 6, **Wild** 6. A tile's mana yield is decided when it's turned over: Steady = 2; Shifting = 1–3 (d6: 1–2 → 1, 3–4 → 2, 5–6 → 3); Wild = 0–4 (d6: 1 → 0, 2 → 1, 3–4 → 2, 5 → 3, 6 → 4). Write the yield on the tile.
3. Each player takes **9 mana**. Play the first test **without traits**.

## The opening (before round 1)

Do this **three times**:
1. **Unveil:** turn over 4 random face-down tiles. The first 3 are auctioned; the 4th stays free. 12 tiles are revealed in total, 3 of them free.
2. **Auction:** each player secretly writes one of the 3 tiles and a bid of at least 1 mana, or "pass". Reveal together.
   - Highest bid on a tile wins it and pays. Losers don't pay.
   - A tie for the top bid: nobody gets that tile; it joins the leftovers.
   - Everyone who didn't win gets a random leftover tile for free.

Each player now holds 3 tiles, scattered or connected.

## A round (8 rounds)

1. **Income:** each player collects the mana yield of every tile they hold.
2. **Upkeep:** pay 1 mana per Golem. A Golem you can't pay for dissolves.
3. **Commit, in secret:** one stance plus any unit moves. Reveal together. (Later Unveils in rounds 2 and 4 happen before income.)
   - **Attune:** claim one free, unguarded tile next to any tile you own. Cost: **1 + 3 × the number of tiles you hold**. If two players attune the same tile, nobody gets it and nobody pays.
   - **Summon:** spend mana on one kind of unit, placed on one of your tiles. **Imp:** power 1, costs 1. **Golem:** power 3, costs 2, upkeep 1.
   - **Attack:** pick a rival's tile or a monster tile next to one of yours, and the units that attack (from your adjacent tiles).
   - **Moves:** any number of units, each one step between your own adjacent tiles, within a connected group.
4. **Resolve:** moves, then Attunes and Summons, then attacks. Units sent to attack don't defend their own tile that round. If several players attack the same tile, they all fight the defender in one battle; if the attackers win, the survivors fight each other for it.
5. **Score:** keep two running counts. Add the **tiles you hold** to one: every **3** there is **1 point**. Add the **mana you produced** to the other: every **10** is **1 point**. Remainders carry over. Plus **2 points per tile taken or cleared this round**.

**Later Unveils:** at the start of rounds 2 and 4, turn over the remaining face-down tiles, split evenly (round 2: 4 tiles, round 4: the last 3). Roll a d6 for each: **1–2 = guarded** by monsters with **power 3**.

## Battles

- Add up battle power on each side. **Higher total wins**; both sides lose power equal to the smaller total. The loser is wiped out.
- The winner loses weakest units first, stopping before a unit that would take the loss past the loser's total.
- **Ties:** both wiped out; the tile stays with the defender. Against monsters, a tie gives the attacker the tile.
- A winning attacker's survivors move into the tile. A player's **last tile can't be taken**: a winning attack kills its defenders, but the tile stays with its owner and no conquest points are scored.

## End

After round 8, most points wins. Tie-break: most tiles held, then most mana in hand.

## Watch for

- Is 9 starting mana enough for the opening? Do bids end up all 1s and 2s, or does someone spend everything?
- Are there enough free tiles to attune after the opening, or does the 19-hex map run out at once?
- Is Attune at 1 + 3 × tiles held too cheap or too dear?
- Do Imps pile up into an unbeatable horde?
- Is anyone ever attacked, or does everyone just attune?
- How long does a round take to explain and to play?
