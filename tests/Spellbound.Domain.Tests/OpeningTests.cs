namespace Spellbound.Domain.Tests;

public class OpeningTests
{
    private static Match NewMatch(int players, int seed = 1) =>
        Match.Start(players, GameRules.Default, new SeededRandom(seed));

    private static List<Bid> AllPass(Match match) => [.. match.Seats.Select(s => Bid.Pass(s.Index))];

    [Theory]
    [InlineData(2, 9, 10)]
    [InlineData(3, 12, 7)]
    [InlineData(4, 15, 22)]
    [InlineData(5, 18, 19)]
    public void The_opening_reveals_three_tiles_per_player_and_three_free_ones(int players, int revealed, int dark)
    {
        var match = NewMatch(players);
        var random = new SeededRandom(7);

        for (var unveil = 1; unveil <= 3; unveil++)
        {
            Assert.Equal(MatchPhase.Opening, match.Phase);
            Assert.Equal(unveil, match.OpeningUnveil);
            Assert.Equal(players, match.OnAuction.Count);
            Assert.True(match.Auction(AllPass(match), random).Succeeded);
        }

        Assert.Equal(MatchPhase.Rounds, match.Phase);
        Assert.Empty(match.OnAuction);
        Assert.Equal(dark, match.Map.Dark.Count());
        Assert.Equal(revealed, match.Map.Tiles.Count(t => t.IsUnveiled));
        Assert.All(match.Seats, s => Assert.Equal(3, s.Tiles.Count));
        Assert.Equal(3, match.Map.Tiles.Count(t => t.IsUnveiled && match.OwnerOf(t.Hex) is null));
    }

    [Fact]
    public void A_new_match_gives_every_seat_its_starting_mana_and_lays_out_the_first_auction()
    {
        var match = NewMatch(3);

        Assert.All(match.Seats, s => Assert.Equal(9, s.Mana));
        Assert.All(match.Seats, s => Assert.Empty(s.Tiles));
        Assert.All(match.OnAuction, h => Assert.True(match.Map[h].IsUnveiled));
        Assert.Equal(4, match.Map.Tiles.Count(t => t.IsUnveiled));
    }

    [Fact]
    public void The_highest_bid_wins_and_pays_and_the_losing_bidder_pays_nothing()
    {
        var match = NewMatch(2);
        var tile = match.OnAuction[0];

        var report = match.Auction([new Bid(0, tile, 4), new Bid(1, tile, 3)], new SeededRandom(2));

        Assert.True(report.Succeeded);
        Assert.Equal(5, match.Seats[0].Mana);
        Assert.Equal(9, match.Seats[1].Mana);
        Assert.Equal(0, match.OwnerOf(tile));
        Assert.Single(match.Seats[1].Tiles);
        Assert.NotEqual(tile, match.Seats[1].Tiles.Single());
        Assert.Contains(report.Lines, l => l.StartsWith("Seat 1 wins", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tie_at_the_top_wins_nobody_the_tile_and_nobody_pays()
    {
        var match = NewMatch(3);
        var contested = match.OnAuction[0];
        var other = match.OnAuction[1];

        var report = match.Auction(
            [new Bid(0, contested, 3), new Bid(1, contested, 3), new Bid(2, other, 1)], new SeededRandom(2));

        Assert.True(report.Succeeded);
        Assert.Equal(9, match.Seats[0].Mana);
        Assert.Equal(9, match.Seats[1].Mana);
        Assert.Equal(8, match.Seats[2].Mana);
        Assert.Equal(2, match.OwnerOf(other));
        Assert.Contains(report.Lines, l => l.StartsWith("A tie", StringComparison.Ordinal));
        // The tied tile became a leftover, so one of the tied seats got it for free.
        Assert.NotNull(match.OwnerOf(contested));
        Assert.NotEqual(2, match.OwnerOf(contested));
    }

    [Fact]
    public void A_lower_tie_does_not_stop_the_top_bid_from_winning()
    {
        var match = NewMatch(3);
        var tile = match.OnAuction[0];

        match.Auction([new Bid(0, tile, 5), new Bid(1, tile, 2), new Bid(2, tile, 2)], new SeededRandom(2));

        Assert.Equal(0, match.OwnerOf(tile));
        Assert.Equal(4, match.Seats[0].Mana);
    }

    [Fact]
    public void Everyone_who_passes_gets_a_free_tile()
    {
        var match = NewMatch(4);
        var onAuction = match.OnAuction.ToList();

        var report = match.Auction(AllPass(match), new SeededRandom(2));

        Assert.True(report.Succeeded);
        Assert.All(match.Seats, s => Assert.Equal(9, s.Mana));
        Assert.All(match.Seats, s => Assert.Contains(s.Tiles.Single(), onAuction));
        Assert.Equal(4, report.Lines.Count(l => l.Contains("for free", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_minimum_bid_wins_an_uncontested_tile()
    {
        var match = NewMatch(2);
        var tile = match.OnAuction[1];

        match.Auction([new Bid(0, tile, 1), Bid.Pass(1)], new SeededRandom(2));

        Assert.Equal(0, match.OwnerOf(tile));
        Assert.Equal(8, match.Seats[0].Mana);
    }

    public static TheoryData<string> BadBids => ["missing", "twice", "unknown seat", "not on auction", "too much", "zero", "negative"];

    [Theory]
    [MemberData(nameof(BadBids))]
    public void An_invalid_set_of_bids_changes_nothing(string problem)
    {
        var match = NewMatch(2);
        var tile = match.OnAuction[0];
        var offAuction = match.Map.Dark.First().Hex;
        List<Bid> bids = problem switch
        {
            "missing" => [Bid.Pass(0)],
            "twice" => [Bid.Pass(0), Bid.Pass(0)],
            "unknown seat" => [Bid.Pass(0), Bid.Pass(2)],
            "not on auction" => [new Bid(0, offAuction, 1), Bid.Pass(1)],
            "too much" => [new Bid(0, tile, 10), Bid.Pass(1)],
            "zero" => [new Bid(0, tile, 0), Bid.Pass(1)],
            _ => [new Bid(0, tile, -1), Bid.Pass(1)],
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
        for (var i = 0; i < 3; i++)
        {
            match.Auction(AllPass(match), random);
        }

        var before = Snapshot(match);

        Assert.False(match.Auction(AllPass(match), random).Succeeded);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void The_same_seed_and_bids_play_out_the_same()
    {
        Assert.Equal(PlayOpening(5), PlayOpening(5));
        Assert.NotEqual(PlayOpening(5), PlayOpening(6));
    }

    private static string PlayOpening(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        var lines = new List<string>();
        while (match.Phase == MatchPhase.Opening)
        {
            var tile = match.OnAuction[0];
            lines.AddRange(match.Auction([new Bid(0, tile, 2), new Bid(1, tile, 1), Bid.Pass(2)], random).Lines);
        }

        return string.Join('\n', lines) + Snapshot(match);
    }

    private static string Snapshot(Match match) =>
        $"{match.Phase} {match.OpeningUnveil} [{string.Join(' ', match.OnAuction)}] "
        + string.Join(" | ", match.Seats.Select(s => $"{s.Mana}: {string.Join(' ', s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R))}"))
        + " / " + string.Join(' ', match.Map.Tiles.Select(t => $"{t.Hex}={t.Yield}"));
}
