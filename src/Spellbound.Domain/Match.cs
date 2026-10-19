namespace Spellbound.Domain;

/// <summary>Where a match is: the opening's auctions, the rounds, or over.</summary>
public enum MatchPhase
{
    Opening,
    Rounds,
    Ended,
}

/// <summary>
/// One game of Spellbound, from the dark map to the end (docs/game-design.md). Everything random comes from the
/// injected source, so the same seed and the same moves always play out the same. Actions either fully succeed or
/// change nothing.
/// </summary>
public sealed partial class Match
{
    private readonly List<Seat> seats;
    private readonly List<Hex> onAuction = [];

    private Match(GameRules rules, GameMap map, List<Seat> seats)
    {
        Rules = rules;
        Map = map;
        this.seats = seats;
    }

    public GameRules Rules { get; }

    public GameMap Map { get; }

    public IReadOnlyList<Seat> Seats => seats;

    public MatchPhase Phase { get; private set; }

    /// <summary>The opening Unveil being auctioned (1 to <see cref="GameRules.OpeningUnveils"/>), or 0 once the
    /// opening is over.</summary>
    public int OpeningUnveil { get; private set; }

    /// <summary>The tiles up for auction in the current opening Unveil.</summary>
    public IReadOnlyList<Hex> OnAuction => onAuction;

    /// <summary>The seat holding a tile, or null if it is unowned.</summary>
    public int? OwnerOf(Hex hex) => seats.FirstOrDefault(s => s.Tiles.Contains(hex))?.Index;

    /// <summary>Starts a match: a dark map for this many players, each seat with its starting mana, and the first
    /// opening Unveil on the table.</summary>
    public static Match Start(int players, GameRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var map = GameMap.Create(players, rules, random);
        var match = new Match(rules, map, [.. Enumerable.Range(0, players).Select(i => new Seat(i, rules.StartingMana))]);
        match.NextOpeningUnveil(random);
        return match;
    }

    /// <summary>
    /// Settles the current opening auction with every seat's sealed bid (docs/game-design.md, "The Unveil auction"):
    /// the highest bid on a tile wins it and pays; a tie at the top wins nobody the tile; losing bidders don't pay.
    /// Everyone who didn't win gets one of the tiles nobody won, at random, for free. Then the next Unveil is laid out,
    /// or after the last one the rounds begin. Fails, changing nothing, unless there is exactly one valid bid per seat.
    /// </summary>
    public Report Auction(IReadOnlyCollection<Bid> bids, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(bids);
        ArgumentNullException.ThrowIfNull(random);
        if (Phase != MatchPhase.Opening)
        {
            return Report.Fail("The opening auctions are over.");
        }

        if (BidProblem(bids) is { } problem)
        {
            return Report.Fail(problem);
        }

        var lines = new List<string>();
        var winners = new Dictionary<int, Hex>();
        foreach (var tile in onAuction)
        {
            var top = bids.Where(b => b.Tile == tile).GroupBy(b => b.Amount).MaxBy(g => g.Key)?.ToList();
            if (top is [var winner])
            {
                winners[winner.Seat] = tile;
                seats[winner.Seat].Mana -= winner.Amount;
                seats[winner.Seat].Take(tile);
                lines.Add($"Seat {winner.Seat + 1} wins {Describe(tile)} for {winner.Amount} mana.");
            }
            else if (top is { Count: > 1 })
            {
                lines.Add($"A tie for {Describe(tile)} at {top[0].Amount} mana: nobody gets it.");
            }
        }

        var leftovers = onAuction.Where(t => !winners.ContainsValue(t)).ToList();
        foreach (var seat in seats.Where(s => !winners.ContainsKey(s.Index)))
        {
            var i = random.Next(leftovers.Count);
            seat.Take(leftovers[i]);
            lines.Add($"Seat {seat.Index + 1} gets {Describe(leftovers[i])} for free.");
            leftovers.RemoveAt(i);
        }

        if (OpeningUnveil < Rules.OpeningUnveils)
        {
            NextOpeningUnveil(random);
        }
        else
        {
            onAuction.Clear();
            OpeningUnveil = 0;
            Phase = MatchPhase.Rounds;
            lines.Add("The opening is over. The rounds begin.");
            lines.AddRange(StartRound(random));
        }

        return new Report(true, lines);
    }

    private string? BidProblem(IReadOnlyCollection<Bid> bids)
    {
        if (bids.Count != seats.Count || bids.Select(b => b.Seat).Distinct().Count() != seats.Count
            || bids.Any(b => b.Seat < 0 || b.Seat >= seats.Count))
        {
            return "Every seat needs exactly one bid or pass.";
        }

        return bids.Select(OneBidProblem).FirstOrDefault(p => p is not null);
    }

    /// <summary>What one seat's bid would cost and get, or why it can't be made. Changes nothing.</summary>
    public Report CheckBid(Bid bid)
    {
        ArgumentNullException.ThrowIfNull(bid);
        if (Phase != MatchPhase.Opening)
        {
            return Report.Fail("The opening auctions are over.");
        }

        if (bid.Seat < 0 || bid.Seat >= seats.Count)
        {
            return Report.Fail($"There is no seat {bid.Seat + 1}.");
        }

        if (OneBidProblem(bid) is { } problem)
        {
            return Report.Fail(problem);
        }

        return bid.Tile is not { } tile
            ? new Report(true, ["Pass: you pay nothing and get one of the tiles nobody wins, at random."])
            : new Report(true,
            [
                $"Bid {bid.Amount} mana on {Describe(tile)}.",
                $"If yours is the only highest bid, you pay {bid.Amount} and keep {seats[bid.Seat].Mana - bid.Amount} mana.",
                "If you're outbid or tied, you pay nothing and get one of the tiles nobody wins, at random.",
            ]);
    }

    private string? OneBidProblem(Bid bid)
    {
        if (bid.Tile is not { } tile)
        {
            return null;
        }

        if (!onAuction.Contains(tile))
        {
            return $"Seat {bid.Seat + 1} bid on {tile}, which isn't up for auction.";
        }

        if (bid.Amount < Rules.MinBid || bid.Amount > seats[bid.Seat].Mana)
        {
            return seats[bid.Seat].Mana < Rules.MinBid
                ? $"Seat {bid.Seat + 1} has too little mana to bid and must pass."
                : $"Seat {bid.Seat + 1} can bid from {Rules.MinBid} to {seats[bid.Seat].Mana} mana, or pass.";
        }

        return null;
    }

    /// <summary>Reveals the next opening Unveil: one tile per seat to auction, then the free ones, all from the dark
    /// tiles at random.</summary>
    private void NextOpeningUnveil(IRandomSource random)
    {
        OpeningUnveil++;
        onAuction.Clear();
        var dark = Map.Dark.Select(t => t.Hex).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        for (var i = 0; i < seats.Count + Rules.OpeningFreeTilesPerUnveil; i++)
        {
            var pick = random.Next(dark.Count);
            Map.Unveil(dark[pick], Rules, random);
            if (i < seats.Count)
            {
                onAuction.Add(dark[pick]);
            }

            dark.RemoveAt(pick);
        }
    }

    private string Describe(Hex hex) =>
        $"the {Map[hex].Type} tile at {hex} (yields {Map[hex].Yield}" + (Map[hex].IsGuarded ? $", guarded by power {Map[hex].Guard})" : ")");
}
