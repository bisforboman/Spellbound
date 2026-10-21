namespace Spellbound.Domain;

/// <summary>The rounds after the opening (docs/game-design.md, Rounds and stances): income and upkeep, then every
/// seat's secret orders resolve together, then points are banked.</summary>
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

    /// <summary>What one seat's orders would cost and do, or why they can't be given. Changes nothing.</summary>
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

        var lines = new List<string>();
        var positions = new Dictionary<Hex, Army>();
        if ((MovesProblem(orders, lines, positions) ?? SpellProblem(orders, lines, positions) ?? StanceProblem(orders, lines, positions)
             ?? ReinforceProblem(orders, lines) ?? GuessProblem(orders, lines)) is { } problem)
        {
            return Report.Fail(problem);
        }

        var mana = seats[orders.Seat].Mana;
        var extras = orders.Cast is not null || orders.Reinforce is not null;
        if (extras && CostOf(orders) > mana)
        {
            return Report.Fail($"Your orders cost {CostOf(orders)} mana together; you have {mana}.");
        }

        if (extras)
        {
            lines.Add($"All together: {CostOf(orders)} mana, leaving {mana - CostOf(orders)}.");
        }

        return new Report(true, lines);
    }

    /// <summary>
    /// Resolves the round with every seat's orders: moves, then Attunes and Summons, then attacks, then points are
    /// banked (docs/game-design.md, Round order). Then the next round starts (with a later Unveil in rounds 2 and 4,
    /// then income and upkeep), or after the last round the match ends. Fails, changing nothing, unless every seat has
    /// exactly one set of orders and all of them check.
    /// </summary>
    public Report Resolve(IReadOnlyCollection<Orders> orders, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(random);
        if (Phase != MatchPhase.Rounds)
        {
            return Report.Fail("Orders are only given during the rounds.");
        }

        if (orders.Count != seats.Count || orders.Select(o => o.Seat).Distinct().Count() != seats.Count)
        {
            return Report.Fail("Every seat needs exactly one set of orders.");
        }

        foreach (var order in orders.OrderBy(o => o.Seat))
        {
            if (Check(order) is { Succeeded: false } failed)
            {
                return failed;
            }
        }

        var lines = new ReportLines($"Round {CurrentRound}.");
        var byseat = orders.OrderBy(o => o.Seat).ToList();
        foreach (var order in byseat.Where(o => o.Guess is not null))
        {
            // Secret: nobody learns of a guess until the end.
            seats[order.Seat].Guess(order.Guess!.Rival, order.Guess.Trait);
        }

        foreach (var move in byseat.SelectMany(o => o.Moves))
        {
            units[move.From] = UnitsOn(move.From) - move.Units;
            units[move.To] = UnitsOn(move.To) + move.Units;
        }

        foreach (var order in byseat.Where(o => o.Moves.Count > 0))
        {
            lines.Add($"Seat {order.Seat + 1} moves {Plural(order.Moves.Count, "group")} of units.", order.Seat, [.. order.Moves.Select(m => m.To)]);
        }

        ResolveAttunes(byseat.Where(o => o.Stance == Stance.Attune && o.Target is not null).ToList(), lines);
        foreach (var order in byseat.Where(o => o.Stance == Stance.Summon && o.Target is not null && o.Count > 0))
        {
            var hex = order.Target!.Value;
            var army = Army.Of(order.Kind, order.Count);
            seats[order.Seat].Mana -= Favoured(Stance.Summon, Rules.CostOf(order.Kind) * order.Count);
            units[hex] = UnitsOn(hex) + army;
            lines.Add($"Seat {order.Seat + 1} summons {army} at {hex}.", order.Seat, hex);
        }

        foreach (var order in byseat.Where(o => o.Reinforce is not null))
        {
            var r = order.Reinforce!;
            var army = Army.Of(r.Kind, r.Count);
            seats[order.Seat].Mana -= Rules.ReinforceCostOf(r.Kind, r.Count);
            units[r.Tile] = UnitsOn(r.Tile) + army;
            lines.Add($"Seat {order.Seat + 1} reinforces {r.Tile} with {army}.", order.Seat, r.Tile);
        }

        ResolveBuilds(byseat, lines);
        ResolveCasts(byseat, lines);
        ResolveAttacks(byseat.Where(o => o.Stance == Stance.Attack && o.Target is not null).ToList(), lines);
        UpdateFavour(byseat, lines);
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
            ScoreGuesses(lines);
            lines.Add($"The match is over. {string.Join(" and ", Winners.Select(s => $"Seat {s.Index + 1}"))} "
                      + (Winners.Count == 1 ? "wins." : "share the win."));
        }

        ClearRoundSpells();
        return lines.ToReport();
    }

    /// <summary>Starts the next round: a later Unveil if it's one of their rounds, then every seat collects its
    /// land's mana and pays its Golems' upkeep. A Golem that can't be paid for dissolves, on the seat's tiles in board
    /// order.</summary>
    private ReportLines StartRound(IRandomSource random)
    {
        CurrentRound++;
        var lines = new ReportLines($"Round {CurrentRound} of {Rules.Rounds} begins.");
        LaterUnveil(random, lines);
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
        var prices = attunes.ToDictionary(o => o.Seat, o => Favoured(Stance.Attune, Rules.AttunePrice(seats[o.Seat].Tiles.Count)));
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
        if (orders.Target is not { } target)
        {
            lines.Add("Nothing to spend this round: only the moves happen.");
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

                var price = Favoured(Stance.Attune, Rules.AttunePrice(seat.Tiles.Count));
                if (price > seat.Mana)
                {
                    return $"Attuning costs {price} mana; you have {seat.Mana}.";
                }

                lines.Add($"Attune {Describe(target)} for {price} mana, leaving {seat.Mana - price}. If a rival attunes it too, nobody gets it.");
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
                if (orders.Count < 0 || orders.Count > (seat.Mana + FavourOf(Stance.Summon)) / each)
                {
                    return $"{orders.Kind}s cost {each} mana each{(FavourOf(Stance.Summon) > 0 ? $", {FavourOf(Stance.Summon)} less in all this round" : "")}; you have {seat.Mana}.";
                }

                var cost = Favoured(Stance.Summon, each * orders.Count);
                lines.Add($"Summon {Army.Of(orders.Kind, orders.Count)} at {target} for {cost} mana, leaving {seat.Mana - cost}.");
                return null;
            }

            case Stance.Attack:
                return AttackProblem(orders, target, lines, positions);

            case Stance.Build:
                return BuildProblem(orders, target, lines);

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
