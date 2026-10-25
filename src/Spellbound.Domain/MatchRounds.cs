namespace Spellbound.Domain;

/// <summary>The rounds after the opening (docs/game-design.md, Rounds and stances): income and upkeep, then the seats
/// pick stances in turn and everyone takes each one, then points are banked.</summary>
public sealed partial class Match
{
    private readonly Dictionary<Hex, Army> units = [];

    /// <summary>The round being played (1 to <see cref="GameRules.Rounds"/>), or 0 before the rounds begin.</summary>
    public int CurrentRound { get; private set; }

    /// <summary>The units on a tile. They belong to the tile's owner.</summary>
    public Army UnitsOn(Hex hex) => units.GetValueOrDefault(hex);

    /// <summary>The seats from first place to last: most points, then most tiles, then most mana in hand.</summary>
    public IReadOnlyList<Seat> Standings =>
        [.. seats.OrderByDescending(s => s.Score).ThenByDescending(s => s.Tiles.Count).ThenByDescending(s => s.Mana)];

    /// <summary>The winners once the match is over: usually one, more if they tie on every tie-break.</summary>
    public IReadOnlyList<Seat> Winners => Phase != MatchPhase.Ended ? [] :
        [.. Standings.TakeWhile(s => (s.Score, s.Tiles.Count, s.Mana) == (Standings[0].Score, Standings[0].Tiles.Count, Standings[0].Mana))];

    /// <summary>What one seat's turn would cost and do, or why it can't be taken. Changes nothing.</summary>
    public Report Check(Orders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if (Phase != MatchPhase.Rounds)
        {
            return Report.Fail("Orders are only given during the rounds.");
        }

        if (orders.Seat < 0 || orders.Seat >= seats.Count)
        {
            return Report.Fail($"There is no seat {orders.Seat + 1}.");
        }

        if (TurnProblem(orders) is { } turn)
        {
            return Report.Fail(turn);
        }

        // The picker's discount, counted as mana in hand while the orders are checked (and taken back after).
        var seat = seats[orders.Seat];
        var discount = DiscountFor(orders);
        seat.Mana += discount;
        try
        {
            var lines = new List<string>();
            var positions = new Dictionary<Hex, Army>();
            if ((MovesProblem(orders, lines, positions) ?? StanceProblem(orders, lines, positions) ?? ReinforceProblem(orders, lines)) is { } problem)
            {
                return Report.Fail(problem);
            }

            if (orders.Reinforce is not null && CostOf(orders) > seat.Mana)
            {
                return Report.Fail($"Your orders cost {CostOf(orders)} mana together; you have {seat.Mana}.");
            }

            if (orders.Reinforce is not null)
            {
                lines.Add($"All together: {CostOf(orders)} mana, leaving {seat.Mana - CostOf(orders)}.");
            }

            if (discount > 0 && orders.Target is not null)
            {
                lines.Add($"As its picker you pay {discount} less: {Math.Max(0, CostOf(orders) - discount)} mana in all.");
            }

            return new Report(true, lines);
        }
        finally
        {
            seat.Mana -= discount;
        }
    }

    /// <summary>
    /// Takes one seat's turn (docs/game-design.md, Rounds and stances): with no stance open, it picks a stance nobody
    /// has picked this round and takes it first, a little cheaper, with any moves; then every other seat in turn takes
    /// that stance too, or passes. Channel is only its picker's. When every seat has picked and the last stance has
    /// gone round, points are banked and the next round starts (with a later Unveil in rounds 2 and 4, then income and
    /// upkeep), or after the last round the match ends. Fails, changing nothing, unless the orders check.
    /// </summary>
    public Report Take(Orders orders, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(random);
        if (Phase == MatchPhase.Rounds && orders.Seat >= 0 && orders.Seat < seats.Count && Actor != orders.Seat)
        {
            return Report.Fail($"It's Seat {Actor + 1}'s turn, not Seat {orders.Seat + 1}'s.");
        }

        if (Check(orders) is { Succeeded: false } failed)
        {
            return failed;
        }

        var seat = seats[orders.Seat];
        var lines = new ReportLines();
        var discount = DiscountFor(orders);
        if (OpenStance is null)
        {
            picks.Add(new StancePick(orders.Stance, orders.Seat));
            acted = 0;
            lines.Add($"Seat {seat.Index + 1} picks {orders.Stance}.");
        }

        seat.Mana += discount;
        var before = seat.Mana;
        Act(orders, lines);
        seat.Mana -= discount - Math.Clamp(before - seat.Mana, 0, discount); // only what the action cost comes off
        acted++;
        if (OpenStance is null && picks.Count == seats.Count)
        {
            EndRound(lines, random);
        }

        return lines.ToReport();
    }

    /// <summary>One seat's action: its moves, then its stance.</summary>
    private void Act(Orders order, ReportLines lines)
    {
        foreach (var move in order.Moves)
        {
            units[move.From] = UnitsOn(move.From) - move.Units;
            units[move.To] = UnitsOn(move.To) + move.Units;
        }

        if (order.Moves.Count > 0)
        {
            lines.Add($"Seat {order.Seat + 1} moves {Plural(order.Moves.Count, "group")} of units.", order.Seat, [.. order.Moves.Select(m => m.To)]);
        }

        if (order.Stance == Stance.Channel)
        {
            seats[order.Seat].Mana += Rules.ChannelMana;
            lines.Add($"Seat {order.Seat + 1} channels {Rules.ChannelMana} mana.", order.Seat);
            return;
        }

        if (order.Target is null)
        {
            lines.Add($"Seat {order.Seat + 1} passes.");
            return;
        }

        List<Orders> one = [order];
        ResolveAttunes([.. one.Where(o => o.Stance == Stance.Attune)], lines);
        if (order is { Stance: Stance.Summon, Count: > 0 })
        {
            var hex = order.Target!.Value;
            var army = Army.Of(order.Kind, order.Count);
            seats[order.Seat].Mana -= Rules.CostOf(order.Kind) * order.Count;
            units[hex] = UnitsOn(hex) + army;
            lines.Add($"Seat {order.Seat + 1} summons {army} at {hex}.", order.Seat, hex);
        }

        if (order.Reinforce is { } r)
        {
            var army = Army.Of(r.Kind, r.Count);
            seats[order.Seat].Mana -= Rules.ReinforceCostOf(r.Kind, r.Count);
            units[r.Tile] = UnitsOn(r.Tile) + army;
            lines.Add($"Seat {order.Seat + 1} reinforces {r.Tile} with {army}.", order.Seat, r.Tile);
        }

        ResolveBuilds(one, lines);
        ResolveCasts(one, lines);
        ResolveAttacks([.. one.Where(o => o.Stance == Stance.Attack)], lines);
    }

    /// <summary>The round is over: points are banked, then the next round starts or the match ends.</summary>
    private void EndRound(ReportLines lines, IRandomSource random)
    {
        BankPoints(lines);
        if (CurrentRound < Rules.Rounds)
        {
            lines.Append(StartRound(random));
        }
        else
        {
            Phase = MatchPhase.Ended;
            ScoreUpgrades(lines);
            ScoreTraits(lines);
            lines.Add($"The match is over. {string.Join(" and ", Winners.Select(s => $"Seat {s.Index + 1}"))} "
                      + (Winners.Count == 1 ? "wins." : "share the win."));
        }

        ClearRoundSpells();
        picks.Clear();
        acted = 0;
    }

    /// <summary>Every held Shifting and Wild tile rolls its yield for this round's income, in board order. The rolls
    /// are public (owner's choice, 24 Oct 2026); Steady tiles always yield the same, so they don't roll.</summary>
    private void RollYields(IRandomSource random, ReportLines lines)
    {
        var rolls = new List<string>();
        foreach (var hex in seats.SelectMany(s => s.Tiles).OrderBy(h => h.Q).ThenBy(h => h.R).Where(h => Rules.Varies(Map[h].Type)))
        {
            Map.Roll(hex, Rules, random);
            rolls.Add($"{hex}: {Map[hex].Yield}"); // short: it shows every round, and the map gives the type
        }

        if (rolls.Count > 0)
        {
            lines.Add($"This round's rolls: {string.Join(", ", rolls)}.");
        }
    }

    /// <summary>Starts the next round: a later Unveil if it's one of their rounds, then the held tiles roll their
    /// yields, and every seat collects its land's mana and pays its Golems' upkeep. A Golem that can't be paid for
    /// dissolves, on the seat's tiles in board order.</summary>
    private ReportLines StartRound(IRandomSource random)
    {
        CurrentRound++;
        var lines = new ReportLines($"Round {CurrentRound} of {Rules.Rounds} begins.");
        LaterUnveil(random, lines);
        RollYields(random, lines);
        foreach (var seat in seats)
        {
            var income = seat.Tiles.Sum(YieldFor);
            seat.Mana += income;
            seat.ProducedThisRound = income;
            lines.Add($"Seat {seat.Index + 1} collects {income} mana.", seat.Index);

            var golems = seat.Tiles.Sum(h => UnitsOn(h).Golems);
            var unpaid = golems - (Rules.GolemUpkeep == 0 ? golems : Math.Min(golems, seat.Mana / Rules.GolemUpkeep));
            seat.Mana -= (golems - unpaid) * Rules.GolemUpkeep;
            if (golems - unpaid > 0)
            {
                lines.Add($"Seat {seat.Index + 1} pays {(golems - unpaid) * Rules.GolemUpkeep} mana upkeep.", seat.Index);
            }

            if (unpaid > 0)
            {
                lines.Add($"Seat {seat.Index + 1} can't pay for {Plural(unpaid, "Golem")}: they dissolve.", seat.Index);
            }

            foreach (var hex in seat.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).Where(h => UnitsOn(h).Golems > 0))
            {
                var lost = Math.Min(unpaid, UnitsOn(hex).Golems);
                units[hex] = UnitsOn(hex) - new Army(0, lost);
                unpaid -= lost;
            }
        }

        return lines;
    }

    /// <summary>Each seat claims the free tile it attuned and pays for it, unless another seat attuned the same tile:
    /// then nobody gets it and nobody pays.</summary>
    private void ResolveAttunes(List<Orders> attunes, ReportLines lines)
    {
        var prices = attunes.ToDictionary(o => o.Seat, o => Rules.AttunePrice(seats[o.Seat].Tiles.Count));
        foreach (var claim in attunes.GroupBy(o => o.Target!.Value))
        {
            if (claim.Count() > 1)
            {
                lines.Add($"{string.Join(" and ", claim.Select(o => $"Seat {o.Seat + 1}"))} {(claim.Count() == 2 ? "both" : "all")} attune {Describe(claim.Key)}: nobody gets it.");
                continue;
            }

            var seat = seats[claim.Single().Seat];
            seat.Mana -= prices[seat.Index];
            seat.Take(claim.Key);
            lines.Add($"Seat {seat.Index + 1} attunes {Describe(claim.Key)} for {prices[seat.Index]} mana.");
        }
    }

    /// <summary>Banks the round's points: for land held and for mana produced (the remainder carries over).</summary>
    private void BankPoints(ReportLines lines)
    {
        foreach (var seat in seats)
        {
            var held = seat.LandCarry + seat.Tiles.Count;
            var land = held / Rules.TilesPerPoint;
            seat.LandCarry = held % Rules.TilesPerPoint;
            var produced = seat.ProducedCarry + seat.ProducedThisRound;
            var mana = produced / Rules.ManaPerPoint;
            var conquest = seat.ConquestPointsThisRound;
            seat.ProducedCarry = produced % Rules.ManaPerPoint;
            seat.ProducedThisRound = 0;
            seat.ConquestPointsThisRound = 0;
            seat.Score += land + mana + conquest;
            lines.Add($"Seat {seat.Index + 1} banks {Plural(land + mana + conquest, "point")} ({land} for land, {mana} for mana"
                      + (conquest > 0 ? $", {conquest} for conquest" : "") + $"), {seat.Score} in all.");
        }
    }

    /// <summary>Checks a seat's moves one by one; <paramref name="positions"/> gets where its units stand after them
    /// (for the tiles the moves touch).</summary>
    private string? MovesProblem(Orders orders, List<string> lines, Dictionary<Hex, Army> positions)
    {
        var seat = seats[orders.Seat];
        var free = new Dictionary<Hex, Army>();
        foreach (var move in orders.Moves)
        {
            if (!seat.Tiles.Contains(move.From) || !seat.Tiles.Contains(move.To))
            {
                return $"Units can only move between your own tiles ({move.From} to {move.To}).";
            }

            if (!move.From.IsNextTo(move.To))
            {
                return $"Units move one step at a time; {move.From} and {move.To} aren't next to each other.";
            }

            // Units that arrived this round have taken their step, so only the ones that started here can leave.
            var left = free.TryGetValue(move.From, out var f) ? f : UnitsOn(move.From);
            if (!move.Units.IsValid || move.Units.IsEmpty || !left.Holds(move.Units))
            {
                return $"There aren't {move.Units} at {move.From} to move.";
            }

            free[move.From] = left - move.Units;
            positions[move.From] = (positions.TryGetValue(move.From, out var p) ? p : UnitsOn(move.From)) - move.Units;
            positions[move.To] = (positions.TryGetValue(move.To, out var q) ? q : UnitsOn(move.To)) + move.Units;
            lines.Add($"Move {move.Units} from {move.From} to {move.To}.");
        }

        return null;
    }

    private string? StanceProblem(Orders orders, List<string> lines, Dictionary<Hex, Army> positions)
    {
        var seat = seats[orders.Seat];
        if (orders.Stance == Stance.Channel)
        {
            if (orders.Target is not null)
            {
                return "Channel isn't aimed at a tile.";
            }

            lines.Add($"Channel: gain {Rules.ChannelMana} mana, leaving you {seat.Mana + Rules.ChannelMana}.");
            return null;
        }

        if (orders.Target is not { } target)
        {
            lines.Add(orders.Moves.Count > 0 ? $"Take {orders.Stance} but aim it at nothing: only the moves happen." : $"Pass on {orders.Stance}.");
            return null;
        }

        if (!Map.Contains(target))
        {
            return $"{target} isn't on the map.";
        }

        switch (orders.Stance)
        {
            case Stance.Attune:
            {
                if (!Map[target].IsUnveiled || OwnerOf(target) is not null)
                {
                    return $"Only a free, unveiled tile can be attuned; {target} isn't one.";
                }

                if (Map[target].IsGuarded)
                {
                    return $"{target} is guarded by monsters: clear it with an attack.";
                }

                if (!seat.Tiles.Any(h => h.IsNextTo(target)))
                {
                    return $"{target} isn't next to any of your tiles.";
                }

                var price = Rules.AttunePrice(seat.Tiles.Count);
                if (price > seat.Mana)
                {
                    return $"Attuning costs {price} mana; you have {seat.Mana}.";
                }

                lines.Add($"Attune {Describe(target)} for {price} mana, leaving {seat.Mana - price}.");
                return null;
            }

            case Stance.Summon:
            {
                if (!seat.Tiles.Contains(target))
                {
                    return $"Units can only be summoned on your own tiles; {target} isn't yours.";
                }

                // Checked by division, so a huge count can't overflow into a cheap one.
                var each = Rules.CostOf(orders.Kind);
                if (orders.Count < 0 || orders.Count > seat.Mana / each)
                {
                    return $"{orders.Kind}s cost {each} mana each; you have {seat.Mana}.";
                }

                var cost = each * orders.Count;
                lines.Add($"Summon {Army.Of(orders.Kind, orders.Count)} at {target} for {cost} mana, leaving {seat.Mana - cost}.");
                return null;
            }

            case Stance.Attack:
                return AttackProblem(orders, target, lines, positions);

            case Stance.Build:
                return BuildProblem(orders, target, lines);

            case Stance.Cast:
                return SpellProblem(orders, target, lines);

            default:
                return $"Unknown stance {orders.Stance}.";
        }
    }

    /// <summary>Checks an attack's reinforcement: an attack, one of your tiles, at least one unit and no more than the
    /// power cap. Reinforcements guard; they can't join this round's attack.</summary>
    private string? ReinforceProblem(Orders orders, List<string> lines)
    {
        if (orders.Reinforce is not { } r)
        {
            return null;
        }

        var seat = seats[orders.Seat];
        if (orders.Stance != Stance.Attack || orders.Target is null)
        {
            return "Reinforcements come with an attack: choose Attack first.";
        }

        if (!seat.Tiles.Contains(r.Tile))
        {
            return $"Reinforcements can only be summoned on your own tiles; {r.Tile} isn't yours.";
        }

        if (r.Count < 1 || r.Count > Rules.ReinforceMaxPower / Rules.PowerOf(r.Kind))
        {
            return $"An attack can bring at most {Rules.ReinforceMaxPower} power of reinforcements "
                   + $"({Rules.ReinforceMaxPower / Rules.PowerOf(r.Kind)} {r.Kind}s).";
        }

        var cost = Rules.ReinforceCostOf(r.Kind, r.Count);
        if (cost > seat.Mana)
        {
            return $"Those reinforcements cost {cost} mana; you have {seat.Mana}.";
        }

        lines.Add($"Reinforce {r.Tile} with {Army.Of(r.Kind, r.Count)} for {cost} mana ({Rules.ReinforceSurcharge} more per unit than a "
                  + "summon): they guard it this round and can't join the attack.");
        return null;
    }

    private static string Plural(int n, string word) => Plural(n, word, word + "s");

    private static string Plural(int n, string word, string words) => n == 1 ? $"1 {word}" : $"{n} {words}";
}
