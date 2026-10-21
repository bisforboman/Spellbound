using System.Text;
using Spellbound.Domain;

namespace Spellbound.Table;

/// <summary>What one seat can see of a match, as text: the same facts the match page shows a player.</summary>
internal static class View
{
    public static string Of(TableState table, int seat)
    {
        var match = table.Load();
        var me = match.Seats[seat];
        var text = new StringBuilder();
        text.AppendLine($"You are Seat {seat + 1} of {match.Seats.Count}. {Phase(match)}");
        text.AppendLine($"You: {me.Mana} mana, {me.Tiles.Count} tiles, {me.Score} points.");
        if (me.Trait is { } trait)
        {
            text.AppendLine($"Your trait: {trait}, {match.GoalText(trait)}");
        }
        else if (me.Offer.Count > 0)
        {
            text.AppendLine("Your trait offer (choose one; nobody else was offered these):");
            foreach (var t in me.Offer)
            {
                text.AppendLine($"  {t}: {match.GoalText(t)}");
            }
        }

        foreach (var rival in match.Seats.Where(s => s.Index != seat))
        {
            var shown = match.Phase != MatchPhase.Opening && rival.Trait is { } rt ? $", trait {rt}" : "";
            text.AppendLine($"Seat {rival.Index + 1}: {rival.Mana} mana, {rival.Tiles.Count} tiles, {rival.Score} points{shown}.");
        }

        text.AppendLine();
        text.AppendLine("The map ((q,r); a tile's six neighbours are (q+1,r) (q-1,r) (q,r+1) (q,r-1) (q+1,r-1) (q-1,r+1)):");
        foreach (var tile in match.Map.Tiles.OrderBy(t => t.Hex.Q).ThenBy(t => t.Hex.R))
        {
            text.AppendLine($"  {Key(tile.Hex)}: {Describe(match, tile, seat)}");
        }

        if (match.OnAuction.Count > 0)
        {
            text.AppendLine($"Up for auction now: {string.Join(", ", match.OnAuction.Select(Key))}.");
        }

        if (table.LastReport.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("What just happened:");
            foreach (var line in table.LastReport)
            {
                text.AppendLine("  " + line);
            }
        }

        text.AppendLine();
        text.AppendLine(Status(table, match, seat));
        return text.ToString();
    }

    /// <summary>Where the match is and what this seat should do next.</summary>
    public static string Status(TableState table, Match match, int seat)
    {
        var me = match.Seats[seat];
        if (match.Phase == MatchPhase.Ended)
        {
            var standings = string.Join("; ", match.Standings.Select(s => $"Seat {s.Index + 1} {s.Score} points"));
            return $"The match is over. {standings}. Winner: {string.Join(" and ", match.Winners.Select(w => $"Seat {w.Index + 1}"))}.";
        }

        if (table.Pending.ContainsKey(seat))
        {
            var waiting = table.AiSeats.Where(s => !table.Pending.ContainsKey(s)).Select(s => $"Seat {s + 1}");
            return $"You have acted this step; waiting for {string.Join(" and ", waiting)}.";
        }

        if (match.Phase == MatchPhase.Opening && me.Offer.Count > 0 && me.Trait is null)
        {
            return "Your move: choose your trait, e.g. {\"trait\": \"" + me.Offer[0] + "\"}. Then bid.";
        }

        return match.Phase == MatchPhase.Opening
            ? "Your move: bid on one tile up for auction, e.g. {\"bid\": \"q,r\", \"amount\": 2}, or {\"pass\": true}."
            : "Your move: this round's orders (see `rules` for the format).";
    }

    private static string Phase(Match match) => match.Phase switch
    {
        MatchPhase.Opening => $"Opening auction {match.OpeningUnveil} of {match.Rules.OpeningUnveils}.",
        MatchPhase.Rounds => $"Round {match.CurrentRound} of {match.Rules.Rounds}.",
        _ => "The match is over.",
    };

    private static string Describe(Match match, Tile tile, int seat)
    {
        if (!tile.IsUnveiled)
        {
            return "dark";
        }

        var owner = match.OwnerOf(tile.Hex) is { } o
            ? o == seat ? "yours" : $"Seat {o + 1}'s"
            : tile.IsGuarded ? $"guarded by monsters, power {tile.Guard}" : "free";
        var units = match.UnitsOn(tile.Hex) is { IsEmpty: false } army ? $", {army}" : "";
        return $"{tile.Type}, yields {tile.Yield}, {owner}{units}";
    }

    public static string Key(Hex hex) => $"({hex.Q},{hex.R})";

    /// <summary>The rules a player needs, with this match's numbers, and how to write an action.</summary>
    public static string Rules(GameRules r) => $$"""
        SPELLBOUND IN BRIEF
        A hex map starts dark. Up to {{r.Rounds}} rounds after an opening; the most points at the end wins (ties: most
        tiles, then most mana). Everyone acts in secret at the same time; then everything resolves together.

        TILES: Steady (always yields 2), Shifting (1-3), Wild (0-4). Each round you collect your tiles' yield in mana.
        Later Unveils (rounds {{string.Join(" and ", r.LaterUnveilRounds)}}) reveal the rest of the map; some new tiles are
        guarded by monsters (power {{r.MonsterPower}}).

        OPENING: you start with {{r.StartingMana}} mana. First choose your trait. Then {{r.OpeningUnveils}} auctions:
        bid mana (at least {{r.MinBid}}) on one tile up for auction, or pass. The only highest bid wins and pays; a tie
        at the top wins nobody the tile; losers pay nothing. Everyone who didn't win gets a random leftover tile free.

        A ROUND: income, then Golem upkeep ({{r.GolemUpkeep}} mana each; unpaid Golems dissolve). Then each player picks
        ONE stance, plus any moves, plus at most ONE spell:
          attune: claim a free, unguarded tile next to yours. Costs {{r.AttuneBasePrice}} + {{r.AttunePricePerTile}} per
            tile you hold. Two players attuning the same tile: nobody gets it, nobody pays.
          summon: units on one of your tiles. Imp: power {{r.ImpPower}}, {{r.ImpCost}} mana. Golem: power
            {{r.GolemPower}}, {{r.GolemCost}} mana, upkeep {{r.GolemUpkeep}}.
          attack: a rival's tile or a monster tile next to your units; say how many units go from each of your tiles next
            to it. Units that attack don't defend their own tile that round.
          hold: none of the above.
          moves: units step once a round to a neighbouring tile of yours. Moves happen first, so moved units can attack.
        Order: moves, attunes, summons, spells, attacks, then points.

        BATTLES: total power against total power. Higher wins; both sides lose power equal to the smaller total (weakest
        units first); the loser is wiped out. A tie wipes out both and the tile stays (against monsters a tie takes it).
        A player's last tile can't be taken. Several attackers on one tile fight the defender together, then each other.

        SPELLS (alongside the stance, paid on top):
          scorch ({{r.ScorchManaPerPower}} mana per power): burn power on a rival's tile next to yours, weakest first.
          ward ({{r.WardCost}} mana): one of your tiles defends with {{r.WardPower}} more power this round.
          leyline ({{r.LeylineManaPerUnit}} per unit, at least {{r.LeylineMinCost}}): move units between any two of your tiles.
          blight ({{r.BlightCost}} mana): a rival's tile next to yours yields nothing in the next round's income.
          lure ({{r.LureCost}} mana): a monster tile next to yours loses {{r.LurePower}} guard power for good (never below {{r.MinGuard}}).

        POINTS, banked every round and never lost: 1 per {{r.TilesPerPoint}} tile-rounds held (tiles held, added up each
        round), 1 per {{r.ManaPerPoint}} mana your land produced, {{r.PointsPerConquest}} per rival tile taken or monster
        tile cleared. Your trait's goal adds up to {{r.TraitGoalCap}} points at the end (hidden until then).

        HOW TO ACT (one JSON object; tiles as "q,r"):
          {"trait": "Weaver"}
          {"bid": "1,-1", "amount": 2}      or      {"pass": true}
          {"stance": "attune", "target": "0,1"}
          {"stance": "summon", "target": "0,1", "kind": "golem", "count": 2}
          {"stance": "attack", "target": "1,0", "send": [{"from": "0,0", "imps": 3, "golems": 1}]}
          {"stance": "hold"}
        Any stance can add "moves": [{"from": "0,0", "to": "0,1", "imps": 2, "golems": 0}]
        and one "spell": {"name": "scorch", "target": "1,0", "count": 2}
                     or {"name": "leyline", "target": "0,0", "to": "2,-1", "imps": 2, "golems": 0}
        `check` shows what an action would cost and do, changing nothing. `act` commits it and waits for the others.
        """;
}
