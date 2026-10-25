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
            var shown = match.Phase != MatchPhase.Opening && rival.Trait is { } rt ? $", trait {rt}" : ""; // open from round 1
            var mana = match.ManaSeenBy(seat, rival.Index) is { } m ? $"{m} mana" : "mana unseen";
            text.AppendLine($"Seat {rival.Index + 1}: {mana}, {rival.Tiles.Count} tiles, {rival.Score} points{shown}.");
        }

        text.AppendLine();
        text.AppendLine("The map ((q,r); a tile's six neighbours are (q+1,r) (q-1,r) (q,r+1) (q,r-1) (q+1,r-1) (q-1,r+1)):");
        foreach (var tile in match.Map.Tiles.OrderBy(t => t.Hex.Q).ThenBy(t => t.Hex.R))
        {
            text.AppendLine($"  {Key(tile.Hex)}: {Describe(match, tile, seat)}");
        }

        if (match.ToPick.Count > 0)
        {
            text.AppendLine($"To pick now (Seat {match.Picker + 1}'s turn): {string.Join(", ", match.ToPick.Select(Key))}.");
        }

        if (table.LastReportFor(match, seat) is { Count: > 0 } seen)
        {
            text.AppendLine();
            text.AppendLine("What just happened (that you could see):");
            foreach (var line in seen)
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
            return "Your move: choose your trait, e.g. {\"trait\": \"" + me.Offer[0] + "\"}. Then pick on your turn.";
        }

        return match.Phase == MatchPhase.Opening
            ? match.Picker == seat
                ? "Your move: pick one of the tiles to pick, e.g. {\"pick\": \"q,r\"}."
                : $"Seat {match.Picker + 1} picks now; wait for your turn."
            : match.Offer is { } offer && offer.Seat == seat ? $"Your move: the die says {offer.Price} mana for the tile at {Key(offer.Tile)}. Pay it ({{\"pay\": true}}) or decline ({{\"pay\": false}})."
            : match.Actor != seat ? $"Seat {match.Actor + 1}'s turn; wait for yours."
            : match.OpenStance is { } open ? $"Your move: Seat {match.Picks[^1].Seat + 1} picked {open}. Take it too, or pass ({{\"stance\": \"{open.ToString().ToLowerInvariant()}\"}} with no target)."
            : $"Your move: pick a stance nobody has picked this round ({string.Join(", ", match.Unpicked).ToLowerInvariant()}).";
    }

    private static string Phase(Match match) => match.Phase switch
    {
        MatchPhase.Opening => $"Opening draft {match.OpeningUnveil} of {match.Rules.OpeningUnveils}, pick {match.PickNumber}.",
        MatchPhase.Rounds => $"Round {match.CurrentRound} of {match.Rules.Rounds}. Picked so far: "
                             + (match.Picks.Count == 0 ? "nothing." : string.Join(", ", match.Picks.Select(p => $"{p.Stance} (Seat {p.Seat + 1})")) + "."),
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
        var units = match.UnitsSeenBy(seat, tile.Hex) is not { } seen
            ? match.OwnerOf(tile.Hex) is not null ? ", units unseen" : ""
            : seen.IsEmpty ? "" : $", {seen}";
        var built = match.UpgradeOn(tile.Hex) is { } upgrade ? $", with a {upgrade}" : "";
        return $"{tile.Type}, yields {match.Rules.YieldRange(tile.Type)} (rolled each round), {owner}{built}{units}";
    }

    public static string Key(Hex hex) => $"({hex.Q},{hex.R})";

    /// <summary>The rules a player needs, with this match's numbers, and how to write an action.</summary>
    public static string Rules(GameRules r) => RulesText(r) + Environment.NewLine + Environment.NewLine + TraitList(r);

    /// <summary>Every trait in the game with its goal: each player is offered their own few, so these are what a
    /// rival might have.</summary>
    private static string TraitList(GameRules r)
    {
        var match = Match.Start(2, r, new SeededRandom(1));
        return "THE TRAITS (each player is offered their own and drafts one; everyone sees every trait from round 1):" + Environment.NewLine
            + string.Join(Environment.NewLine, Enum.GetValues<Trait>().Select(t => $"  {t}: {match.GoalText(t)}"));
    }

    private static string RulesText(GameRules r) => $$"""
        SPELLBOUND IN BRIEF
        A hex map starts dark. Up to {{r.Rounds}} rounds after an opening; the most points at the end wins (ties: most
        tiles, then most mana). Players take turns, in the open, like Puerto Rico's roles.

        TILES: Steady (always yields 2), Shifting (1-3), Wild (0-4). Each round you collect your tiles' yield in mana.
        Later Unveils (rounds {{string.Join(" and ", r.LaterUnveilRounds)}}) reveal the rest of the map; some new tiles are
        guarded by monsters (power {{r.MonsterPower}}).

        OPENING: you start with {{r.StartingMana}} mana. First choose your trait. Then {{r.OpeningUnveils}} drafts:
        each reveals one tile per player, and players take one each in turn, for free ({"pick": "q,r"} on your turn).
        The first draft starts with seat 1, each next one a seat later; the last to pick gets the tile left over.

        A ROUND: income, then Golem upkeep ({{r.GolemUpkeep}} mana each; unpaid Golems dissolve). Then the players pick
        stances in turn, one each, none twice in a round; the first picker moves one seat each round. When you pick a
        stance you take it first, {{r.PickerDiscount}} mana cheaper, with any moves; then every other player in turn may take it
        too (at full price, no moves) or pass. Then the next player picks.
          attune: claim any free, unguarded tile. Its base price is {{r.AttuneBasePrice}} + {{r.AttunePricePerTile}} for
            each of the first {{r.AttuneCheapTiles}} tiles you hold, + {{r.AttunePricePerTileBeyond}} for each tile beyond them,
            + {{r.AttuneDistanceCost}} for each step beyond the first from your nearest tile. Taking it rolls a die
            (1-{{r.AttuneDieFaces}}) onto the base; you see the price, then pay it ({"pay": true}) or decline ({"pay": false}).
          summon: units on one of your tiles. Imp: power {{r.ImpPower}}, {{r.ImpCost}} mana. Golem: power
            {{r.GolemPower}}, {{r.GolemCost}} mana, upkeep {{r.GolemUpkeep}}.
          attack: a rival's tile or a monster tile next to your units; say how many units go from each of your tiles next
            to it. Units that attack don't defend their own tile that round.
            An attack may also bring reinforcements: up to {{r.ReinforceMaxPower}} power of units summoned on one of your tiles,
            {{r.ReinforceSurcharge}} mana more each than a summon. They guard it this round and can't join the attack.
          build: an upgrade on one of your tiles, {{r.UpgradeCost}} mana, at most one per tile and {{r.UpgradeCap}} per realm.
            Shrine: the tile yields {{r.ShrineYield}} more. Tower: it defends with {{r.TowerPower}} more power, even empty.
            Sanctum: your spells aimed at it or next to it cost {{r.SanctumDiscount}} less. An upgrade passes with the
            tile when it's taken, and is worth {{r.PointsPerUpgrade}} point at the end to whoever holds it.
          cast: one spell (below).
          channel: gain {{r.ChannelMana}} mana. Only its picker takes it.
          moves: with your own pick only; units step once to a neighbouring tile of yours, before the stance.
        Each turn happens at once; points are banked when the round's last stance has gone round.

        BATTLES: total power against total power. Higher wins; both sides lose power equal to the smaller total (weakest
        units first, stopping before a unit that would go past it: losing 2 power can't kill a Golem of power 3); the
        loser is wiped out. Scorch burns the same way. A tie wipes out both and the tile stays (against monsters a tie takes it).
        A player's last tile can't be taken.

        SPELLS (the cast stance, one a round). Your trait decides your spell: {{string.Join("; ", r.TraitSpells.Select(t => $"{t.Key} {string.Join("/", t.Value)}"))}}.
        Ward, Blight and Lure can go on several tiles in one cast ("also"), each paid for.
          scorch ({{r.ScorchManaPerPower}} mana per power that burns): burn power on a rival's tile next to yours, weakest
            first; you pay only for what burns.
          ward ({{r.WardCost}} mana): one of your tiles defends with {{r.WardPower}} more power this round.
          leyline ({{r.LeylineManaPerUnit}} per unit, at least {{r.LeylineMinCost}}): move units between any two of your tiles.
          blight ({{r.BlightCost}} mana): a rival's tile next to yours yields nothing in the next round's income; not the
            same tile two rounds running.
          lure ({{r.LureCost}} mana): a monster tile next to yours loses {{r.LurePower}} guard power for good (never below {{r.MinGuard}}).

        POINTS, banked every round and never lost: 1 per {{r.TilesPerPoint}} tile-rounds held (tiles held, added up each
        round), 1 per {{r.ManaPerPoint}} mana your land produced, {{r.PointsPerConquest}} per monster tile cleared or
        defended rival tile taken (it had units, a Ward or a Tower), {{r.PointsPerEmptyConquest}} per undefended rival tile taken. Your trait's goal adds up to {{r.TraitGoalCap}} points, scored only at the end.

        FOG OF WAR: you see rivals' units only on tiles next to your land, and rivals' mana only at the end. Who holds
        each tile, yields, upgrades and scores are seen by all. The report shows only what you could see.

        TRAITS ARE SECRET until the end. No trait is offered to two players, so a rival never has one you were offered.

        HOW TO ACT (one JSON object; tiles as "q,r"):
          {"trait": "Weaver"}
          {"pick": "1,-1"}                  (in the opening, on your turn)
          {"stance": "attune", "target": "0,1"}
          {"stance": "summon", "target": "0,1", "kind": "golem", "count": 2}
          {"stance": "attack", "target": "1,0", "send": [{"from": "0,0", "imps": 3, "golems": 1}]}
          {"stance": "build", "target": "0,1", "upgrade": "tower"}
          {"stance": "cast", "spell": "scorch", "target": "1,0", "count": 2}
          {"stance": "cast", "spell": "ward", "target": "0,0", "also": ["0,1"]}
          {"stance": "cast", "spell": "leyline", "target": "0,0", "to": "2,-1", "imps": 2, "golems": 0}
          {"stance": "channel"}
          {"stance": "attack"}               (a stance with no target: pass on it, or pick it to keep it from the others)
        Your pick can add "moves": [{"from": "0,0", "to": "0,1", "imps": 2, "golems": 0}]
        An attack can add "reinforce": {"target": "0,0", "kind": "imp", "count": 3}
        `check` shows what an action would cost and do, changing nothing. `act` takes your turn and waits for your next.
        """;
}
