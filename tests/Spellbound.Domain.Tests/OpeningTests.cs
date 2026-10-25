namespace Spellbound.Domain.Tests;

public class OpeningTests
{
    private static Match NewMatch(int players, int seed = 1) =>
        Match.Start(players, GameRules.Default, new SeededRandom(seed));

    private static Pick First(Match match) => new(match.Picker!.Value, match.ToPick[0]);

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
            Assert.Equal(players, match.ToPick.Count);
            Assert.Equal(players * unveil, match.Map.Tiles.Count(t => t.IsUnveiled)); // only the draft's tiles
            for (var pick = 1; pick < players; pick++)
            {
                Assert.Equal(MatchPhase.Opening, match.Phase);
                Assert.Equal(pick, match.PickNumber);
                Assert.True(match.Draft(First(match), random).Succeeded);
            }
        }

        Assert.Equal(MatchPhase.Rounds, match.Phase);
        Assert.Empty(match.ToPick);
        Assert.Equal(dark, match.Map.Dark.Count());
        Assert.Equal(revealed, match.Map.Tiles.Count(t => t.IsUnveiled));
        Assert.All(match.Seats, s => Assert.Equal(3, s.Tiles.Count));
        Assert.Equal(3, match.Map.Tiles.Count(t => t.IsUnveiled && match.OwnerOf(t.Hex) is null));
    }

    [Fact]
    public void A_new_match_reveals_one_tile_per_seat_in_name_order_and_seat_1_picks_first()
    {
        var match = NewMatch(3);

        Assert.All(match.Seats, s => Assert.Equal(4, s.Mana));
        Assert.All(match.Seats, s => Assert.Empty(s.Tiles));
        Assert.Equal(3, match.Map.Tiles.Count(t => t.IsUnveiled));
        Assert.Equal(match.ToPick.OrderBy(h => h.R).ThenBy(h => h.Q), match.ToPick);
        Assert.Equal(0, match.Picker);
    }

    [Fact]
    public void Each_draft_goes_round_the_table_starting_one_seat_later()
    {
        var match = NewMatch(3);
        var random = new SeededRandom(2);
        var order = new List<int>();
        while (match.Phase == MatchPhase.Opening)
        {
            order.Add(match.Picker!.Value);
            match.Draft(First(match), random);
        }

        // The last pick of each draft isn't made: the tile left over goes to the last seat.
        Assert.Equal([0, 1, 1, 2, 2, 0], order);
    }

    [Fact]
    public void A_pick_takes_the_tile_for_free_and_the_last_tile_goes_to_the_last_seat()
    {
        var match = NewMatch(3);
        var (first, second, third) = (match.ToPick[0], match.ToPick[1], match.ToPick[2]);

        var report = match.Draft(new Pick(0, second), new SeededRandom(2));

        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        Assert.Equal(0, match.OwnerOf(second));
        Assert.Equal(4, match.Seats[0].Mana); // picks cost nothing
        Assert.Equal(1, match.Picker);
        Assert.Equal([first, third], match.ToPick);
        Assert.StartsWith("Seat 1 picks the", report.Lines[0], StringComparison.Ordinal);

        report = match.Draft(new Pick(1, third), new SeededRandom(2));

        Assert.Equal(1, match.OwnerOf(third));
        Assert.Equal(2, match.OwnerOf(first)); // the tile left over
        Assert.Contains(report.Lines, l => l.StartsWith("Seat 3 gets the last tile", StringComparison.Ordinal));
        Assert.Equal(2, match.OpeningUnveil);
        Assert.Equal(1, match.Picker);
    }

    public static TheoryData<string> BadPicks => ["not your turn", "unknown seat", "not on the table", "already taken"];

    [Theory]
    [MemberData(nameof(BadPicks))]
    public void An_invalid_pick_changes_nothing(string problem)
    {
        var match = NewMatch(3);
        var taken = match.ToPick[0];
        if (problem == "already taken")
        {
            match.Draft(new Pick(0, taken), new SeededRandom(2));
        }

        var pick = problem switch
        {
            "not your turn" => new Pick(1, match.ToPick[0]),
            "unknown seat" => new Pick(3, match.ToPick[0]),
            "not on the table" => new Pick(0, match.Map.Dark.First().Hex),
            _ => new Pick(1, taken),
        };
        var before = Snapshot(match);

        var report = match.Draft(pick, new SeededRandom(2));

        Assert.False(report.Succeeded);
        Assert.Single(report.Lines);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Picks_after_the_opening_change_nothing()
    {
        var match = NewMatch(2);
        var random = new SeededRandom(3);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Draft(First(match), random);
        }

        var before = Snapshot(match);

        Assert.False(match.Draft(new Pick(0, match.Map.Tiles.First().Hex), random).Succeeded);
        Assert.Null(match.Picker);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void A_saved_match_keeps_whose_turn_it_is()
    {
        var match = NewMatch(3);
        match.Draft(First(match), new SeededRandom(2));

        var loaded = Match.Load(match.Save(), match.Rules);

        Assert.Equal(1, loaded.Picker);
        Assert.Equal(match.ToPick, loaded.ToPick);
        Assert.Equal(2, loaded.PickNumber);
    }

    [Fact]
    public void The_same_seed_and_picks_play_out_the_same()
    {
        Assert.Equal(PlayOpening(5), PlayOpening(5));
        Assert.NotEqual(PlayOpening(5), PlayOpening(6));
    }

    [Fact]
    public void CheckPick_shows_what_a_pick_gets_without_changing_anything()
    {
        var match = NewMatch(3);
        var tile = match.ToPick[1];
        var before = Snapshot(match);

        var check = match.CheckPick(new Pick(0, tile));

        Assert.True(check.Succeeded);
        Assert.StartsWith($"Pick the {match.Map[tile].Type} tile at {tile}", check.Lines[0], StringComparison.Ordinal);
        Assert.Contains("costs nothing", check.Lines[0], StringComparison.Ordinal);
        Assert.Equal("Then Seat 2 picks from the rest.", check.Lines[1]);
        Assert.Equal(before, Snapshot(match));
    }

    [Theory]
    [InlineData(1, 0, "It's Seat 1's turn")]
    [InlineData(0, -1, "isn't one of the tiles to pick from")]
    public void CheckPick_refuses_a_pick_that_cant_be_made(int seat, int tile, string reason)
    {
        var match = NewMatch(3);
        var hex = tile < 0 ? match.Map.Dark.First().Hex : match.ToPick[tile];

        Assert.Contains(reason, Assert.Single(match.CheckPick(new Pick(seat, hex)).Lines), StringComparison.Ordinal);
    }

    private static string PlayOpening(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        var lines = new List<string>();
        while (match.Phase == MatchPhase.Opening)
        {
            lines.AddRange(match.Draft(new Pick(match.Picker!.Value, match.ToPick[^1]), random).Lines);
        }

        return string.Join('\n', lines) + Snapshot(match);
    }

    private static string Snapshot(Match match) =>
        $"{match.Phase} {match.OpeningUnveil} [{string.Join(' ', match.ToPick)}] {match.Picker} "
        + string.Join(" | ", match.Seats.Select(s => $"{s.Mana}: {string.Join(' ', s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R))}"))
        + " / " + string.Join(' ', match.Map.Tiles.Select(t => $"{t.Hex}={t.Yield}"));
}
