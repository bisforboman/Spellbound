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
    private readonly HashSet<int> unveilWinners = [];

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

    /// <summary>The tiles of the current opening Unveil still to be handed out, in the order they go up (by name: row,
    /// then place in the row). The first is the one being bid on.</summary>
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
        match.DealTraits(random);
        return match;
    }

    /// <summary>
    /// Settles the bidding on the current tile (docs/game-design.md, "The Unveil auction"): every seat still bidding in
    /// this Unveil bids secretly or passes. The highest bid wins the tile and pays; a tie at the top is settled at
    /// random among the tied; if nobody bids, a seat still bidding gets it free, at random. The winner sits out the rest
    /// of the Unveil, and the last tile goes free to the one seat left. Then the next Unveil is laid out, or after the
    /// last one the rounds begin. Fails, changing nothing, unless there is exactly one valid bid per seat still bidding.
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

        var lines = new ReportLines();
        var tile = onAuction[0];
        // Every bid is revealed once the tile is settled (owner's choice, 24 Oct 2026), so players learn how rivals value land.
        lines.Add($"Bids on {Describe(tile)}: {string.Join(", ", bids.OrderBy(b => b.Seat).Select(b => b.Tile is null ? $"Seat {b.Seat + 1} passes" : $"Seat {b.Seat + 1} {b.Amount}"))}.");
        var top = bids.Where(b => b.Tile == tile).GroupBy(b => b.Amount).MaxBy(g => g.Key)?.OrderBy(b => b.Seat).ToList();
        if (top is { Count: > 0 })
        {
            var winner = top[top.Count == 1 ? 0 : random.Next(top.Count)];
            if (top.Count > 1)
            {
                lines.Add($"A tie for {Describe(tile)} at {winner.Amount} mana: {string.Join(" and ", top.Select(b => $"Seat {b.Seat + 1}"))} bid it, and Seat {winner.Seat + 1} is drawn.");
            }

            seats[winner.Seat].Mana -= winner.Amount;
            Hand(tile, winner.Seat, lines, $"Seat {winner.Seat + 1} wins {Describe(tile)} for {winner.Amount} mana.");
        }
        else
        {
            var bidders = Bidders.ToList();
            var lucky = bidders[random.Next(bidders.Count)];
            Hand(tile, lucky, lines, $"Nobody bids on {Describe(tile)}: Seat {lucky + 1} gets it for free, drawn at random.");
        }

        if (onAuction.Count == 1)
        {
            var last = Bidders.Single();
            var leftover = onAuction[0];
            Hand(leftover, last, lines, $"Seat {last + 1} gets the last tile, {Describe(leftover)}, for free.");
        }

        if (onAuction.Count > 0)
        {
            return lines.ToReport();
        }

        if (OpeningUnveil < Rules.OpeningUnveils)
        {
            NextOpeningUnveil(random);
        }
        else
        {
            OpeningUnveil = 0;
            Phase = MatchPhase.Rounds;
            lines.Add("The opening is over. The rounds begin.");
            RevealFreeTiles(random, lines);
            SettleTraits(lines);
            lines.Append(StartRound(random));
        }

        return lines.ToReport();
    }

    /// <summary>The seats still bidding in this opening Unveil: everyone who hasn't got a tile from it yet.</summary>
    public IEnumerable<int> Bidders => Phase == MatchPhase.Opening
        ? seats.Where(s => !unveilWinners.Contains(s.Index)).Select(s => s.Index)
        : [];

    /// <summary>Which of this Unveil's tiles is up now, counting from 1.</summary>
    public int AuctionTile => seats.Count - onAuction.Count + 1;

    /// <summary>Gives a tile from the auction to a seat, which then sits out the rest of the Unveil.</summary>
    private void Hand(Hex tile, int seat, ReportLines lines, string line)
    {
        seats[seat].Take(tile);
        unveilWinners.Add(seat);
        onAuction.Remove(tile);
        lines.Add(line);
    }

    private string? BidProblem(IReadOnlyCollection<Bid> bids)
    {
        var bidders = Bidders.ToHashSet();
        if (bids.Count != bidders.Count || !bids.Select(b => b.Seat).ToHashSet().SetEquals(bidders))
        {
            return "Every seat still bidding needs exactly one bid or pass.";
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
            ? new Report(true, ["Pass: you pay nothing and bid again on the next tile. Whoever wins none gets the last one free."])
            : new Report(true,
            [
                $"Bid {bid.Amount} mana on {Describe(tile)}.",
                $"If yours is the highest bid, you pay {bid.Amount} and keep {seats[bid.Seat].Mana - bid.Amount} mana. A tie is drawn at random.",
                "If you're outbid, you pay nothing and bid again on the next tile. Whoever wins none gets the last one free.",
            ]);
    }

    private string? OneBidProblem(Bid bid)
    {
        if (unveilWinners.Contains(bid.Seat))
        {
            return $"Seat {bid.Seat + 1} already has a tile from this Unveil.";
        }

        if (bid.Tile is not { } tile)
        {
            return null;
        }

        if (onAuction.Count == 0 || onAuction[0] != tile)
        {
            return $"Seat {bid.Seat + 1} bid on {tile}, which isn't the tile up for auction now.";
        }

        if (bid.Amount < Rules.MinBid || bid.Amount > seats[bid.Seat].Mana)
        {
            return seats[bid.Seat].Mana < Rules.MinBid
                ? $"Seat {bid.Seat + 1} has too little mana to bid and must pass."
                : $"Seat {bid.Seat + 1} can bid from {Rules.MinBid} to {seats[bid.Seat].Mana} mana, or pass.";
        }

        return null;
    }

    /// <summary>Reveals the next opening Unveil: one tile per seat, from the dark tiles at random, to go up one at a
    /// time by name.</summary>
    private void NextOpeningUnveil(IRandomSource random)
    {
        OpeningUnveil++;
        onAuction.Clear();
        unveilWinners.Clear();
        onAuction.AddRange(RevealDark(seats.Count, random).OrderBy(h => h.R).ThenBy(h => h.Q));
    }

    /// <summary>As the rounds begin, the free tiles are revealed: unowned, for anyone to attune (the opening itself is
    /// only auctions; owner's choice, 24 Oct 2026).</summary>
    private void RevealFreeTiles(IRandomSource random, ReportLines lines)
    {
        var free = RevealDark(Rules.FreeTilesWhenRoundsBegin, random).OrderBy(h => h.R).ThenBy(h => h.Q).ToList();
        if (free.Count > 0)
        {
            lines.Add($"Free to attune: {string.Join("; ", free.Select(Describe))}.");
        }
    }

    /// <summary>Turns over this many dark tiles, picked at random (never guarded: only later Unveils have monsters).</summary>
    private List<Hex> RevealDark(int count, IRandomSource random)
    {
        var dark = Map.Dark.Select(t => t.Hex).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var revealed = new List<Hex>();
        for (var i = 0; i < count && dark.Count > 0; i++)
        {
            var pick = random.Next(dark.Count);
            Map.Unveil(dark[pick], Rules, random);
            revealed.Add(dark[pick]);
            dark.RemoveAt(pick);
        }

        return revealed;
    }

    private string Describe(Hex hex) =>
        $"the {Map[hex].Type} tile at {hex} (yields {Rules.YieldRange(Map[hex].Type)}" + (Map[hex].IsGuarded ? $", guarded by power {Map[hex].Guard})" : ")");

    /// <summary>A tile's type, place and yield, for lines that say who holds it separately.</summary>
    private string DescribePlain(Hex hex) => $"the {Map[hex].Type} tile at {hex} (yields {Rules.YieldRange(Map[hex].Type)})";
}
