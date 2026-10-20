namespace Spellbound.Domain.Tests;

/// <summary>Matches set up for a test: past the opening (everyone passed), on a seed whose layout fits.</summary>
internal static class Scenarios
{
    public static Match Opened(int seed, GameRules? rules = null, int players = 3)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(players, rules ?? GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Seats.Select(s => Bid.Pass(s.Index))], random);
        }

        return match;
    }

    /// <summary>The first seeded match where the condition holds, so tests don't depend on one seed's layout.</summary>
    public static Match FirstWhere(Func<Match, bool> condition, GameRules? rules = null, int players = 3) =>
        Opened(Enumerable.Range(1, 2000).First(seed => condition(Opened(seed, rules, players))), rules, players);

    /// <summary>Plays a round: these orders, and every other seat holds.</summary>
    public static Report Play(Match match, params Orders[] orders)
    {
        var report = match.Resolve(
            [.. orders, .. match.Seats.Where(s => orders.All(o => o.Seat != s.Index)).Select(s => Orders.Hold(s.Index))],
            new SeededRandom(match.CurrentRound));
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
