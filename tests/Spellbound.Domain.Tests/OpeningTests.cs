namespace Spellbound.Domain.Tests;

public class OpeningTests
{
    private static Match NewMatch(int players, int seed = 1) =>
        Match.Start(players, GameRules.Default, new SeededRandom(seed));

    private static List<Bid> AllPass(Match match) => [.. match.Bidders.Select(Bid.Pass)];

    [Theory]
    [InlineData(2, 9, 10)]
    [InlineData(3, 12, 7)]
    [InlineData(4, 15, 22)]
    [InlineData(5, 18, 19)]
    public void The_opening_hands_out_three_tiles_per_player_and_three_free_ones_appear_as_the_rounds_begin(int players, int revealed, int dark)
    {
        var match = NewMatch(players);
        var random = new SeededRandom(7);

        for (var unveil = 1; unveil <= 3; unveil++)
        {
            Assert.Equal(unveil, match.OpeningUnveil);
            Assert.Equal(players, match.OnAuction.Count);
            Assert.Equal(players * unveil, match.Map.Tiles.Count(t => t.IsUnveiled)); // only the auction's tiles
            for (var tile = 1; tile < players; tile++)
            {
                Assert.Equal(MatchPhase.Opening, match.Phase);
                Assert.Equal(tile, match.AuctionTile);
                Assert.True(match.Auction(AllPass(match), random).Succeeded);
            }
        }

        Assert.Equal(MatchPhase.Rounds, match.Phase);
        Assert.Empty(match.OnAuction);
        Assert.Equal(dark, match.Map.Dark.Count());
        Assert.Equal(revealed, match.Map.Tiles.Count(t => t.IsUnveiled));
        Assert.All(match.Seats, s => Assert.Equal(3, s.Tiles.Count));
        Assert.Equal(3, match.Map.Tiles.Count(t => t.IsUnveiled && match.OwnerOf(t.Hex) is null));
    }

    [Fact]
    public void A_new_match_reveals_one_tile_per_seat_in_name_order()
    {
        var match = NewMatch(3);

        Assert.All(match.Seats, s => Assert.Equal(9, s.Mana));
        Assert.All(match.Seats, s => Assert.Empty(s.Tiles));
        Assert.Equal(3, match.Map.Tiles.Count(t => t.IsUnveiled));
        Assert.Equal(match.OnAuction.OrderBy(h => h.R).ThenBy(h => h.Q), match.OnAuction);
        Assert.Equal([0, 1, 2], match.Bidders);
    }

    [Fact]
    public void The_highest_bid_wins_the_tile_up_now_and_the_winner_sits_out_the_rest()
    {
        var match = NewMatch(3);
        var (first, second, third) = (match.OnAuction[0], match.OnAuction[1], match.OnAuction[2]);

        var report = match.Auction([new Bid(0, first, 4), new Bid(1, first, 3), Bid.Pass(2)], new SeededRandom(2));

        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        Assert.Equal(0, match.OwnerOf(first));
        Assert.Equal(5, match.Seats[0].Mana);
        Assert.Equal(9, match.Seats[1].Mana); // a losing bid costs nothing
        Assert.Equal([1, 2], match.Bidders);
        Assert.Equal(second, match.OnAuction[0]);
        Assert.Contains(report.Lines, l => l.StartsWith("Seat 1 wins", StringComparison.Ordinal));
        Assert.Equal($"Bids on the {match.Map[first].Type} tile at {first} (yields {match.Rules.YieldRange(match.Map[first].Type)}): Seat 1 4, Seat 2 3, Seat 3 passes.", report.Lines[0]); // revealed once settled

        match.Auction([Bid.Pass(1), new Bid(2, second, 1)], new SeededRandom(2));

        Assert.Equal(2, match.OwnerOf(second));
        Assert.Equal(1, match.OwnerOf(third)); // lost every bid, so the last tile is theirs, free
        Assert.Equal(9, match.Seats[1].Mana);
        Assert.Equal(2, match.OpeningUnveil);
        Assert.Equal([0, 1, 2], match.Bidders);
    }

    [Fact]
    public void A_tie_at_the_top_is_drawn_at_random_and_only_the_winner_pays()
    {
        var winners = new HashSet<int>();
        for (var seed = 1; seed <= 20; seed++)
        {
            var match = NewMatch(3);
            var tile = match.OnAuction[0];

            var report = match.Auction([new Bid(0, tile, 3), new Bid(1, tile, 3), new Bid(2, tile, 1)], new SeededRandom(seed));

            var winner = match.OwnerOf(tile)!.Value;
            Assert.Contains(winner, new[] { 0, 1 });
            Assert.Equal(6, match.Seats[winner].Mana);
            Assert.Equal(9, match.Seats[1 - winner].Mana);
            Assert.Contains(report.Lines, l => l.StartsWith("A tie", StringComparison.Ordinal));
            winners.Add(winner);
        }

        Assert.Equal(2, winners.Count); // either can win
    }

    [Fact]
    public void A_tile_nobody_bids_on_goes_free_to_a_seat_still_bidding()
    {
        var match = NewMatch(4);
        var tile = match.OnAuction[0];

        var report = match.Auction(AllPass(match), new SeededRandom(2));

        Assert.True(report.Succeeded);
        Assert.All(match.Seats, s => Assert.Equal(9, s.Mana));
        Assert.NotNull(match.OwnerOf(tile));
        Assert.Equal(3, match.Bidders.Count());
        Assert.Contains(report.Lines, l => l.StartsWith("Nobody bids on", StringComparison.Ordinal));
    }

    [Fact]
    public void The_minimum_bid_wins_an_uncontested_tile()
    {
        var match = NewMatch(2);
        var tile = match.OnAuction[0];

        match.Auction([new Bid(0, tile, 1), Bid.Pass(1)], new SeededRandom(2));

        Assert.Equal(0, match.OwnerOf(tile));
        Assert.Equal(8, match.Seats[0].Mana);
    }

    public static TheoryData<string> BadBids =>
        ["missing", "twice", "unknown seat", "a later tile", "not on auction", "too much", "zero", "negative", "already won"];

    [Theory]
    [MemberData(nameof(BadBids))]
    public void An_invalid_set_of_bids_changes_nothing(string problem)
    {
        var match = NewMatch(3);
        var tile = match.OnAuction[0];
        if (problem == "already won")
        {
            match.Auction([new Bid(0, tile, 1), Bid.Pass(1), Bid.Pass(2)], new SeededRandom(2));
            tile = match.OnAuction[0];
        }

        var offAuction = match.Map.Dark.First().Hex;
        List<Bid> bids = problem switch
        {
            "missing" => [Bid.Pass(0), Bid.Pass(1)],
            "twice" => [Bid.Pass(0), Bid.Pass(0), Bid.Pass(1)],
            "unknown seat" => [Bid.Pass(0), Bid.Pass(1), Bid.Pass(3)],
            "a later tile" => [new Bid(0, match.OnAuction[1], 1), Bid.Pass(1), Bid.Pass(2)],
            "not on auction" => [new Bid(0, offAuction, 1), Bid.Pass(1), Bid.Pass(2)],
            "too much" => [new Bid(0, tile, 10), Bid.Pass(1), Bid.Pass(2)],
            "zero" => [new Bid(0, tile, 0), Bid.Pass(1), Bid.Pass(2)],
            "negative" => [new Bid(0, tile, -1), Bid.Pass(1), Bid.Pass(2)],
            _ => [new Bid(0, tile, 1), Bid.Pass(1), Bid.Pass(2)], // seat 1 has its tile from this Unveil
        };
        var before = Snapshot(match);

        var report = match.Auction(bids, new SeededRandom(2));

        Assert.False(report.Succeeded);
        Assert.Single(report.Lines);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Auctions_after_the_opening_change_nothing()
    {
        var match = NewMatch(2);
        var random = new SeededRandom(3);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction(AllPass(match), random);
        }

        var before = Snapshot(match);

        Assert.False(match.Auction([Bid.Pass(0), Bid.Pass(1)], random).Succeeded);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void A_saved_match_keeps_who_has_bid_their_way_out_of_the_unveil()
    {
        var match = NewMatch(3);
        match.Auction([new Bid(0, match.OnAuction[0], 2), Bid.Pass(1), Bid.Pass(2)], new SeededRandom(2));

        var loaded = Match.Load(match.Save(), match.Rules);

        Assert.Equal([1, 2], loaded.Bidders);
        Assert.Equal(match.OnAuction, loaded.OnAuction);
        Assert.Equal(2, loaded.AuctionTile);
    }

    [Fact]
    public void The_same_seed_and_bids_play_out_the_same()
    {
        Assert.Equal(PlayOpening(5), PlayOpening(5));
        Assert.NotEqual(PlayOpening(5), PlayOpening(6));
    }

    [Fact]
    public void CheckBid_shows_what_a_bid_costs_and_gets_without_changing_anything()
    {
        var match = NewMatch(3);
        var tile = match.OnAuction[0];
        var before = Snapshot(match);

        var bid = match.CheckBid(new Bid(1, tile, 3));
        var pass = match.CheckBid(Bid.Pass(1));

        Assert.True(bid.Succeeded);
        Assert.StartsWith("Bid 3 mana on the", bid.Lines[0], StringComparison.Ordinal);
        Assert.Contains("keep 6 mana", bid.Lines[1], StringComparison.Ordinal);
        Assert.True(pass.Succeeded);
        Assert.Contains("next tile", pass.Lines[0], StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }

    [Theory]
    [InlineData(0, "can bid from 1 to 9")]
    [InlineData(10, "can bid from 1 to 9")]
    [InlineData(-1, "isn't the tile up for auction now")]
    [InlineData(-2, "There is no seat 4")]
    [InlineData(-3, "isn't the tile up for auction now")]
    public void CheckBid_refuses_a_bid_that_cant_be_made(int amount, string reason)
    {
        var match = NewMatch(3);
        var bid = amount switch
        {
            -1 => new Bid(0, match.Map.Dark.First().Hex, 1),
            -2 => new Bid(3, match.OnAuction[0], 1),
            -3 => new Bid(0, match.OnAuction[2], 1),
            _ => new Bid(0, match.OnAuction[0], amount),
        };

        Assert.Contains(reason, Assert.Single(match.CheckBid(bid).Lines), StringComparison.Ordinal);
    }

    private static string PlayOpening(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        var lines = new List<string>();
        while (match.Phase == MatchPhase.Opening)
        {
            var tile = match.OnAuction[0];
            List<Bid> bids = [.. match.Bidders.Select(s => s == 2 ? Bid.Pass(s) : new Bid(s, tile, 1))];
            lines.AddRange(match.Auction(bids, random).Lines);
        }

        return string.Join('\n', lines) + Snapshot(match);
    }

    private static string Snapshot(Match match) =>
        $"{match.Phase} {match.OpeningUnveil} [{string.Join(' ', match.OnAuction)}] [{string.Join(' ', match.Bidders)}] "
        + string.Join(" | ", match.Seats.Select(s => $"{s.Mana}: {string.Join(' ', s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R))}"))
        + " / " + string.Join(' ', match.Map.Tiles.Select(t => $"{t.Hex}={t.Yield}"));
}
