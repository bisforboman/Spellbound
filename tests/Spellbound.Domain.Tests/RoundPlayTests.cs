namespace Spellbound.Domain.Tests;

public class RoundPlayTests
{
    /// <summary>A match past its opening (everyone passed), at the start of round 1.</summary>
    private static Match Opened(int players = 3, int seed = 1, GameRules? rules = null)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(players, rules ?? GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Seats.Select(s => Bid.Pass(s.Index))], random);
        }

        return match;
    }

    private static List<Orders> HoldAll(Match match, params Orders[] orders) =>
        [.. orders, .. match.Seats.Where(s => orders.All(o => o.Seat != s.Index)).Select(s => Orders.Hold(s.Index))];

    private static IEnumerable<Hex> FreeNeighbours(Match match, int seat) =>
        match.Seats[seat].Tiles.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.Map[h].IsUnveiled && match.OwnerOf(h) is null).OrderBy(h => h.Q).ThenBy(h => h.R);

    /// <summary>The first seeded match where the condition holds, so tests don't depend on one seed's layout.</summary>
    private static Match FirstWhere(Func<Match, bool> condition, GameRules? rules = null) =>
        Opened(3, Enumerable.Range(1, 500).First(seed => condition(Opened(3, seed))), rules);

    private static string Snapshot(Match match) =>
        $"{match.Phase} {match.CurrentRound} "
        + string.Join(" | ", match.Seats.Select(s => $"{s.Mana} {s.Score} {s.ProducedThisRound} {s.ProducedCarry} {s.LandCarry}: "
            + string.Join(' ', s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).Select(h => $"{h}{match.UnitsOn(h)}"))));

    [Fact]
    public void The_first_round_begins_after_the_opening_with_income()
    {
        var match = Opened();

        Assert.Equal(MatchPhase.Rounds, match.Phase);
        Assert.Equal(1, match.CurrentRound);
        Assert.All(match.Seats, s => Assert.Equal(9 + s.Tiles.Sum(h => match.Map[h].Yield!.Value), s.Mana));
    }

    [Fact]
    public void Held_wild_and_shifting_tiles_roll_their_yield_every_round_and_steady_ones_dont()
    {
        // Wild tiles that only ever roll 0 or 4, so a few rounds show both.
        var rules = GameRules.Default with { WildYields = [0, 4], ShiftingYields = [0, 4] };
        var match = FirstWhere(m => m.Seats[0].Tiles.Any(h => m.Map[h].Type == TileType.Wild)
                                    && m.Seats[0].Tiles.Any(h => m.Map[h].Type == TileType.Steady), rules);
        var wild = match.Seats[0].Tiles.First(h => match.Map[h].Type == TileType.Wild);
        var steady = match.Seats[0].Tiles.First(h => match.Map[h].Type == TileType.Steady);
        var rolls = new HashSet<int>();

        for (var round = 0; round < 6; round++)
        {
            var mana = match.Seats[0].Mana;
            var report = match.Resolve(HoldAll(match), new SeededRandom(100 + round));

            rolls.Add(match.Map[wild].Yield!.Value);
            Assert.Equal(2, match.Map[steady].Yield);
            Assert.Equal(mana + match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value), match.Seats[0].Mana); // the income is the rolls
            var line = Assert.Single(report.Lines, l => l.StartsWith("This round's rolls: ", StringComparison.Ordinal));
            Assert.Contains($"Wild {wild} rolls {match.Map[wild].Yield}", line, StringComparison.Ordinal);
            Assert.DoesNotContain($"{steady}", line, StringComparison.Ordinal);
        }

        Assert.Equal([0, 4], rolls.Order());
    }

    [Fact]
    public void A_tile_is_described_by_its_range_not_its_latest_roll()
    {
        var seed = Enumerable.Range(1, 500).First(n =>
            Match.Start(3, GameRules.Default, new SeededRandom(n)) is var m && m.OnAuction.Any(h => m.Map[h].Type == TileType.Wild));
        var match = Match.Start(3, GameRules.Default, new SeededRandom(seed));
        var wild = match.OnAuction.First(h => match.Map[h].Type == TileType.Wild);

        var check = match.CheckBid(new Bid(0, wild, 1));

        Assert.Contains($"the Wild tile at {wild} (yields 0–4)", check.Lines[0], StringComparison.Ordinal);
        Assert.Equal("1–3", match.Rules.YieldRange(TileType.Shifting));
        Assert.Equal("2", match.Rules.YieldRange(TileType.Steady));
    }

    [Fact]
    public void Summoning_places_units_on_your_tile_and_costs_their_price()
    {
        var match = Opened();
        var tile = match.Seats[0].Tiles.First();
        var mana = match.Seats[0].Mana;

        var report = match.Resolve(HoldAll(match, Orders.Summon(0, tile, UnitKind.Golem, 2)), new SeededRandom(7));

        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        Assert.Equal(new Army(0, 2), match.UnitsOn(tile));
        var income = match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value);
        Assert.Equal(mana - 4 + income - 2, match.Seats[0].Mana); // two Golems cost 4, then 2 upkeep next round
        Assert.Contains(report.Lines, l => l.Contains("pays 2 mana upkeep", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Golem_that_cant_be_paid_for_dissolves()
    {
        var match = Opened(rules: GameRules.Default with { GolemUpkeep = 100 });
        var tile = match.Seats[0].Tiles.First();

        var report = match.Resolve(HoldAll(match, Orders.Summon(0, tile, UnitKind.Golem, 1)), new SeededRandom(7));

        Assert.Equal(Army.None, match.UnitsOn(tile));
        Assert.Contains(report.Lines, l => l.Contains("can't pay for 1 Golem", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 4)]
    [InlineData(4, 7)]
    [InlineData(5, 10)]
    public void Attune_is_cheap_for_a_small_realm_and_dear_beyond_three_tiles(int tiles, int price) =>
        Assert.Equal(price, GameRules.Default.AttunePrice(tiles));

    [Fact]
    public void Attuning_claims_a_free_tile_next_to_yours_for_one_plus_one_per_tile_held_up_to_three()
    {
        var match = FirstWhere(m => FreeNeighbours(m, 0).Any());
        var target = FreeNeighbours(match, 0).First();
        var mana = match.Seats[0].Mana;

        var check = match.Check(Orders.Attune(0, target));
        var report = match.Resolve(HoldAll(match, Orders.Attune(0, target)), new SeededRandom(7));

        Assert.Contains(check.Lines, l => l.Contains("for 4 mana", StringComparison.Ordinal));
        Assert.True(report.Succeeded);
        Assert.Equal(0, match.OwnerOf(target));
        Assert.Equal(4, match.Seats[0].Tiles.Count);
        var income = match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value);
        Assert.Equal(mana - 4 + income, match.Seats[0].Mana);
    }

    [Fact]
    public void Two_seats_attuning_the_same_tile_get_nothing_and_pay_nothing()
    {
        var match = FirstWhere(m => FreeNeighbours(m, 0).Intersect(FreeNeighbours(m, 1)).Any());
        var target = FreeNeighbours(match, 0).Intersect(FreeNeighbours(match, 1)).First();
        var before = match.Seats.Select(s => s.Mana - s.Tiles.Sum(h => match.Map[h].Yield!.Value)).ToList();

        var report = match.Resolve(HoldAll(match, Orders.Attune(0, target), Orders.Attune(1, target)), new SeededRandom(7));

        Assert.True(report.Succeeded);
        Assert.Null(match.OwnerOf(target));
        Assert.Contains(report.Lines, l => l.Contains("nobody gets it", StringComparison.Ordinal));
        Assert.Equal(before[0] + 2 * match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value), match.Seats[0].Mana);
    }

    [Fact]
    public void Units_move_one_step_between_your_own_tiles()
    {
        var match = FirstWhere(m => FreeNeighbours(m, 0).Any());
        var target = FreeNeighbours(match, 0).First();
        var home = match.Seats[0].Tiles.First(h => h.IsNextTo(target));
        match.Resolve(HoldAll(match, Orders.Summon(0, home, UnitKind.Imp, 3)), new SeededRandom(7));
        match.Resolve(HoldAll(match, Orders.Attune(0, target)), new SeededRandom(7));

        var report = match.Resolve(HoldAll(match, Orders.Hold(0, new Move(home, target, new Army(2, 0)))), new SeededRandom(7));

        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        Assert.Equal(new Army(1, 0), match.UnitsOn(home));
        Assert.Equal(new Army(2, 0), match.UnitsOn(target));
    }

    [Fact]
    public void Units_that_moved_this_round_cant_move_again()
    {
        var match = FirstWhere(m => FreeNeighbours(m, 0).Any());
        var target = FreeNeighbours(match, 0).First();
        var home = match.Seats[0].Tiles.First(h => h.IsNextTo(target));
        match.Resolve(HoldAll(match, Orders.Summon(0, home, UnitKind.Imp, 2)), new SeededRandom(7));
        match.Resolve(HoldAll(match, Orders.Attune(0, target)), new SeededRandom(7));
        var before = Snapshot(match);

        var there = new Move(home, target, new Army(2, 0));
        var back = new Move(target, home, new Army(2, 0));
        var report = match.Resolve(HoldAll(match, Orders.Hold(0, there, back)), new SeededRandom(7));

        Assert.False(report.Succeeded);
        Assert.Equal(before, Snapshot(match));
    }

    public static TheoryData<string, string> BadOrders => new()
    {
        { "summon on a rival's tile", "only be summoned on your own tiles" },
        { "summon too many", "cost 2 mana each" },
        { "attune an owned tile", "Only a free, unveiled tile" },
        { "attune far away", "isn't next to any of your tiles" },
        { "attune too dear", "Attuning costs 103 mana" },
        { "move off your land", "only move between your own tiles" },
        { "move units you don't have", "There aren't 1 Imp" },
        { "move two steps", "aren't next to each other" },
        { "a seat missing", "exactly one set of orders" },
        { "a seat twice", "exactly one set of orders" },
        { "no such seat", "There is no seat 4" },
    };

    [Theory]
    [MemberData(nameof(BadOrders))]
    public void Orders_that_dont_check_change_nothing(string problem, string reason)
    {
        var match = FirstWhere(m => FreeNeighbours(m, 0).Any()
            && m.Map.Tiles.Any(t => t.IsUnveiled && m.OwnerOf(t.Hex) is null && !m.Seats[0].Tiles.Any(h => h.IsNextTo(t.Hex)))
            && m.Seats[0].Tiles.Any(a => m.Seats[0].Tiles.Any(b => !a.IsNextTo(b) && a != b))
            && m.Seats[0].Tiles.Any(a => m.Seats[0].Tiles.Any(a.IsNextTo)),
            // The opening doesn't depend on the Attune price, so this is the same match with Attunes out of reach.
            problem == "attune too dear" ? GameRules.Default with { AttuneBasePrice = 100 } : null);
        var mine = match.Seats[0].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var near = mine.SelectMany(a => mine.Where(a.IsNextTo).Select(b => (a, b))).First();
        var far = mine.SelectMany(a => mine.Where(b => a != b && !a.IsNextTo(b)).Select(b => (a, b))).First();
        var rival = match.Seats[1].Tiles.First();
        var lonely = match.Map.Tiles.First(t => t.IsUnveiled && match.OwnerOf(t.Hex) is null
            && !mine.Any(h => h.IsNextTo(t.Hex))).Hex;
        var free = FreeNeighbours(match, 0).First();
        List<Orders> orders = problem switch
        {
            "summon on a rival's tile" => HoldAll(match, Orders.Summon(0, rival, UnitKind.Imp, 1)),
            "summon too many" => HoldAll(match, Orders.Summon(0, mine[0], UnitKind.Golem, int.MaxValue)),
            "attune an owned tile" => HoldAll(match, Orders.Attune(0, rival)),
            "attune far away" => HoldAll(match, Orders.Attune(0, lonely)),
            "attune too dear" => HoldAll(match, Orders.Attune(0, free)),
            "move off your land" => HoldAll(match, Orders.Hold(0, new Move(mine[0], rival, new Army(1, 0)))),
            "move units you don't have" => HoldAll(match, Orders.Hold(0, new Move(near.a, near.b, new Army(1, 0)))),
            "move two steps" => HoldAll(match, Orders.Hold(0, new Move(far.a, far.b, new Army(1, 0)))),
            "a seat missing" => [Orders.Hold(0), Orders.Hold(1)],
            "a seat twice" => [Orders.Hold(0), Orders.Hold(1), Orders.Hold(1)],
            _ => [Orders.Hold(0), Orders.Hold(1), Orders.Hold(3)],
        };
        var before = Snapshot(match);

        var report = match.Resolve(orders, new SeededRandom(7));

        Assert.False(report.Succeeded);
        Assert.Contains(reason, Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Check_shows_the_cost_and_result_without_changing_anything()
    {
        var match = Opened();
        var tile = match.Seats[0].Tiles.First();
        var before = Snapshot(match);

        var report = match.Check(Orders.Summon(0, tile, UnitKind.Imp, 3));

        Assert.True(report.Succeeded);
        Assert.Contains(report.Lines, l => l.StartsWith("Summon 3 Imps", StringComparison.Ordinal) && l.Contains("for 3 mana", StringComparison.Ordinal));
        Assert.Equal(before, Snapshot(match));
        Assert.False(match.Check(Orders.Summon(0, tile, UnitKind.Imp, match.Seats[0].Mana + 1)).Succeeded);
    }

    [Fact]
    public void Points_are_banked_for_every_three_tile_rounds_and_twelve_mana_with_the_rest_carried()
    {
        var match = Opened();
        var produced = match.Seats.Select(s => s.Tiles.Sum(h => match.Map[h].Yield!.Value)).ToList();

        match.Resolve(HoldAll(match), new SeededRandom(7));

        // Three tiles for one round: 3 tile-rounds, 1 point and nothing carried.
        Assert.All(match.Seats, s =>
        {
            Assert.Equal(1 + produced[s.Index] / 12, s.Score);
            Assert.Equal(0, s.LandCarry);
            Assert.Equal(produced[s.Index] % 12, s.ProducedCarry);
        });
    }

    [Fact]
    public void Land_that_isnt_worth_a_point_yet_carries_to_the_next_round()
    {
        var match = Opened(rules: GameRules.Default with { TilesPerPoint = 2, ManaPerPoint = 1000 });

        match.Resolve(HoldAll(match), new SeededRandom(7));
        Assert.All(match.Seats, s => Assert.Equal((1, 1), (s.Score, s.LandCarry))); // 3 tile-rounds: 1 point, 1 left

        match.Resolve(HoldAll(match), new SeededRandom(7));
        Assert.All(match.Seats, s => Assert.Equal((3, 0), (s.Score, s.LandCarry))); // 1 + 3 more: 2 points, none left
    }

    [Fact]
    public void After_the_last_round_the_match_ends_with_a_winner_and_takes_no_more_orders()
    {
        var match = Opened();
        for (var round = 1; round <= 8; round++)
        {
            Assert.Equal(round, match.CurrentRound);
            Assert.True(match.Resolve(HoldAll(match), new SeededRandom(7)).Succeeded);
        }

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.NotEmpty(match.Winners);
        Assert.Equal(match.Standings[0], match.Winners[0]);
        var before = Snapshot(match);
        Assert.False(match.Resolve(HoldAll(match), new SeededRandom(7)).Succeeded);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Orders_are_refused_during_the_opening()
    {
        var match = Match.Start(2, GameRules.Default, new SeededRandom(1));

        Assert.False(match.Resolve([Orders.Hold(0), Orders.Hold(1)], new SeededRandom(7)).Succeeded);
        Assert.False(match.Check(Orders.Hold(0)).Succeeded);
    }

    [Fact]
    public void The_same_seed_and_orders_play_out_the_same()
    {
        Assert.Equal(PlayPeacefully(4), PlayPeacefully(4));
    }

    private static string PlayPeacefully(int seed)
    {
        var match = Opened(3, seed);
        var lines = new List<string>();
        while (match.Phase == MatchPhase.Rounds)
        {
            var orders = match.Seats.Select(s => FreeNeighbours(match, s.Index).Cast<Hex?>().FirstOrDefault() is { } h
                && match.Check(Orders.Attune(s.Index, h)).Succeeded
                    ? Orders.Attune(s.Index, h)
                    : Orders.Summon(s.Index, s.Tiles.First(), UnitKind.Imp, Math.Min(2, s.Mana))).ToList();
            lines.AddRange(match.Resolve(orders, new SeededRandom(7)).Lines);
        }

        return string.Join('\n', lines) + Snapshot(match);
    }
}
