namespace Spellbound.Domain.Tests;

/// <summary>Matches set up for a test: past the opening (everyone passed), on a seed whose layout fits.</summary>
internal static class Scenarios
{
    /// <remarks>Without a picker's discount, so a test of what an action costs sees its price whoever picks it;
    /// StanceDraftTests covers the discount.</remarks>
    public static Match Opened(int seed, GameRules? rules = null, int players = 3)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(players, (rules ?? GameRules.Default) with { PickerDiscount = 0 }, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Draft(new Pick(match.Picker!.Value, match.ToPick[0]), random);
        }

        return match;
    }

    /// <summary>The first seeded match where the condition holds, so tests don't depend on one seed's layout.</summary>
    public static Match FirstWhere(Func<Match, bool> condition, GameRules? rules = null, int players = 3) =>
        Opened(Enumerable.Range(1, 2000).First(seed => condition(Opened(seed, rules, players))), rules, players);

    /// <summary>Rules where every trait can cast every spell, for tests of how a spell works rather than who may cast it.</summary>
    public static GameRules AnySpell(GameRules? rules = null) => (rules ?? GameRules.Default) with
    {
        TraitSpells = Enum.GetValues<Trait>().ToDictionary(t => t, IReadOnlyList<Spell> (_) => GameRules.AllSpells),
    };

    /// <summary>Orders for a seat that only wants its moves (if any): on its pick it takes a stance nobody else wants,
    /// aimed at nothing, and it passes on everyone else's.</summary>
    public static Orders Hold(int seat, params IReadOnlyList<Move> moves) => new(seat, HoldStance, null, moves);

    private const Stance HoldStance = (Stance)(-1);

    /// <summary>
    /// Plays the rest of the round from what each seat wants: a seat picks the stance of its orders when its pick comes
    /// (with its moves), unless another seat picked it first, and then takes it when it goes round; every other turn it
    /// passes, and a pick it doesn't need goes to a stance nobody wants (Channel last). Returns every turn's lines, or the
    /// first turn that failed.
    /// </summary>
    public static Report Resolve(this Match match, IReadOnlyCollection<Orders> orders, IRandomSource random)
    {
        if (match.Phase != MatchPhase.Rounds)
        {
            return match.Check(orders.First());
        }

        var want = orders.ToDictionary(o => o.Seat);
        var done = new HashSet<int>();
        var lines = new List<string>();
        var tags = new List<LineTag?>();
        var round = match.CurrentRound;
        while (match.Phase == MatchPhase.Rounds && match.CurrentRound == round)
        {
            var seat = match.Actor!.Value;
            var mine = want.GetValueOrDefault(seat) is { } o && o.Stance != HoldStance && !done.Contains(seat) ? o : null;
            Orders turn;
            if (match.OpenStance is { } open)
            {
                turn = mine is not null && mine.Stance == open ? mine : Orders.Pass(seat, open);
            }
            else if (mine is not null && match.Unpicked.Contains(mine.Stance))
            {
                turn = mine;
            }
            else
            {
                var wanted = want.Values.Where(w => !done.Contains(w.Seat)).Select(w => w.Stance).ToHashSet();
                var filler = match.Unpicked.OrderBy(st => st == Stance.Channel).First(st => !wanted.Contains(st) || st == match.Unpicked[^1]);
                turn = Orders.Pass(seat, filler, want.GetValueOrDefault(seat)?.Stance == HoldStance ? want[seat].Moves : []);
            }

            if (turn == mine || (turn.Moves.Count > 0 && want.GetValueOrDefault(seat)?.Stance == HoldStance))
            {
                done.Add(seat);
            }

            var report = match.Take(turn, random);
            if (report.Succeeded && match.Offer is { } offer && offer.Seat == seat)
            {
                report = report.Then(match.Settle(seat, offer.Price <= match.Seats[seat].Mana, random)); // pays when it can
            }

            if (!report.Succeeded)
            {
                return report;
            }

            lines.AddRange(report.Lines);
            tags.AddRange(report.Lines.Select((_, i) => report.TagOf(i)));
        }

        return new Report(true, lines, tags);
    }

    /// <summary>Plays a round: these orders, and every other seat holds.</summary>
    public static Report Play(Match match, params Orders[] orders)
    {
        var report = match.Resolve(orders, new SeededRandom(match.CurrentRound));
        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        return report;
    }

    /// <summary>A tile of <paramref name="seat"/> next to a tile of <paramref name="rival"/>, if any.</summary>
    public static (Hex Mine, Hex Theirs)? Border(Match match, int seat, int rival) =>
        match.Seats[seat].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R)
            .SelectMany(a => match.Seats[rival].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).Where(a.IsNextTo).Select(b => (a, b)))
            .Cast<(Hex, Hex)?>().FirstOrDefault();

    public static string Snapshot(Match match) =>
        $"{match.Phase} {match.CurrentRound} "
        + string.Join(" | ", match.Seats.Select(s => $"{s.Mana} {s.Score}: "
            + string.Join(' ', s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).Select(h => $"{h}{match.UnitsOn(h)}"))))
        + " / " + string.Join(' ', match.Map.Tiles.Select(t => $"{t.Yield}{t.Guard}"));
}
