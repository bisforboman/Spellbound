using Spellbound.Domain;
using Spellbound.Domain.Skirmish;

namespace Spellbound.Domain.Tests;

public sealed class SkirmishBoardTests
{
    [Fact]
    public void Boards_are_connected_with_spread_out_homes_of_one_terrain()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var players = 2 + (seed % 5);
            var board = SkirmishBoard.Generate(players, SkirmishRules.Default, new SeededRandom(seed));

            Assert.Equal(players * SkirmishRules.Default.RegionsPerPlayer, board.Regions.Count);
            Assert.Equal(board.Regions.Count, Reachable(board, 0));
            Assert.Equal(players, board.Homes.Distinct().Count());
            Assert.All(board.Homes, h => Assert.Equal(SkirmishTerrain.T1, board[h].Terrain));
            Assert.All(board.Homes, h => Assert.DoesNotContain(board[h].Neighbours, board.Homes.Contains));
            Assert.All(board.Regions, r => Assert.All(r.Neighbours, n => Assert.Contains(r.Id, board[n].Neighbours)));
        }
    }

    [Fact]
    public void The_same_seed_makes_the_same_board()
    {
        var a = SkirmishBoard.Generate(4, SkirmishRules.Default, new SeededRandom(42));
        var b = SkirmishBoard.Generate(4, SkirmishRules.Default, new SeededRandom(42));

        Assert.Equal(a.Regions.Select(r => (r.Terrain, string.Join(',', r.Neighbours))), b.Regions.Select(r => (r.Terrain, string.Join(',', r.Neighbours))));
    }

    private static int Reachable(SkirmishBoard board, int from)
    {
        var seen = new HashSet<int> { from };
        var queue = new Queue<int>([from]);
        while (queue.TryDequeue(out var id))
        {
            foreach (var next in board[id].Neighbours.Where(seen.Add))
            {
                queue.Enqueue(next);
            }
        }

        return seen.Count;
    }
}

public sealed class SkirmishGameTests
{
    /// <summary>Two seats on a ring of four regions and no extra links: homes 0 and 2, and 1 and 3 between them.</summary>
    private static readonly SkirmishRules Small = SkirmishRules.Default with { RegionsPerPlayer = 2, ExtraLinks = 0, BoardShareToWin = 0.5 };

    /// <summary>Two seats on a ring of six: homes 0 and 3; 1 and 5 are next to seat 0, 2 and 4 next to seat 1.</summary>
    private static readonly SkirmishRules Six = SkirmishRules.Default with { RegionsPerPlayer = 3, ExtraLinks = 0 };

    private static SkirmishGame NewGame(SkirmishRules rules, int seed = 1) =>
        SkirmishGame.Start([("Merlin", MagicSchool.A), ("Morgana", MagicSchool.B)], rules, new SeededRandom(seed));

    private static Dictionary<int, IReadOnlyList<SkirmishOrder>> Orders(params SkirmishOrder[] seat0) => new() { [0] = seat0 };

    [Fact]
    public void A_game_starts_with_homes_buildings_and_a_full_deck()
    {
        var game = NewGame(SkirmishRules.Default);

        Assert.Equal(0, game.Board[game.Board.Homes[0]].Owner);
        Assert.Equal(1, game.Board[game.Board.Homes[1]].Owner);
        Assert.Equal((1, 1), (game.Board[game.Board.Homes[0]].Nodes, game.Board[game.Board.Homes[0]].Towns));
        Assert.Equal(30, game.Deck.Count);
        Assert.Equal(12, game.DeckCounts[DiscoveryCard.Card1]);
        Assert.Equal(SkirmishRules.Default.StartingGold, game.Seats[0].Gold);
    }

    [Fact]
    public void Claiming_takes_the_region_and_draws_a_card()
    {
        var game = NewGame(Small);
        var card = game.Deck[0];

        var report = game.PlayRound(Orders(new ClaimOrder(1)), new SeededRandom(0));

        Assert.Equal(0, game.Board[1].Owner);
        Assert.Equal(29, game.Deck.Count);
        Assert.Contains($"drew {card.DisplayName()}", report.BySeat[0][0], StringComparison.Ordinal);
        Assert.Contains("Merlin claimed Region 2", report.Public[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_claim_that_is_not_possible_spends_the_action_and_changes_nothing_else()
    {
        var game = NewGame(Six);
        var control = NewGame(Six);

        var report = game.PlayRound(Orders(new ClaimOrder(2), new ClaimOrder(3)), new SeededRandom(0)); // not next to us; held
        control.PlayRound(Orders(), new SeededRandom(0));

        Assert.Equal("Claim Region 3: not done. Region 3 isn't next to one of your regions.", report.BySeat[0][0]);
        Assert.Equal("Claim Region 4: not done. Region 4 is already held.", report.BySeat[0][1]);
        Assert.Equal(Snapshot(control), Snapshot(game));
    }

    [Fact]
    public void Two_claims_on_one_region_are_settled_by_a_roll_and_the_loser_spends_the_action()
    {
        var orders = new Dictionary<int, IReadOnlyList<SkirmishOrder>> { [0] = [new ClaimOrder(1)], [1] = [new ClaimOrder(1)] };
        var first = NewGame(Small);
        var second = NewGame(Small);

        Assert.Equal(0.5, first.ClaimOdds([0, 1])[0]);
        var won = first.PlayRound(orders, new FixedRandom(0.25));
        var lost = second.PlayRound(orders, new FixedRandom(0.75));

        Assert.Equal(0, first.Board[1].Owner);
        Assert.Equal(1, second.Board[1].Owner);
        Assert.Equal("Region 2 was claimed by Merlin (50%), Morgana (50%). Merlin got it.", won.Public[0]);
        Assert.Contains(lost.BySeat[0], l => l == "You lost the contest for Region 2; the action is spent.");
        Assert.Equal(29, second.Deck.Count); // only the winner draws
    }

    [Fact]
    public void An_army_improves_the_odds_of_a_contest()
    {
        var game = NewGame(Small);
        game.PlayRound(Orders(new RecruitOrder("unit-3", 5)), new SeededRandom(0)); // attack 30

        Assert.Equal(40.0 / 50, game.ClaimOdds([0, 1])[0], 3);
    }

    [Fact]
    public void Building_in_a_region_claimed_the_same_round_is_allowed()
    {
        var game = NewGame(Small);
        SkirmishOrder[] orders = [new ClaimOrder(1), new BuildOrder(1, BuildingType.Town, 2)];

        Assert.Null(game.CheckOrders(0, orders));
        var report = game.PlayRound(Orders(orders), new SeededRandom(0));

        Assert.Equal(2, game.Board[1].Towns);
        Assert.Contains(report.BySeat[0], l => l.StartsWith("You built 2 towns in Region 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_build_in_a_region_lost_to_a_contest_is_reported_and_costs_nothing()
    {
        var orders = new Dictionary<int, IReadOnlyList<SkirmishOrder>>
        {
            [0] = [new ClaimOrder(1), new BuildOrder(1, BuildingType.Town, 2)],
            [1] = [new ClaimOrder(1)],
        };
        var game = NewGame(Small);
        var gold = game.Seats[0].Gold;

        var report = game.PlayRound(orders, new FixedRandom(0.75));

        Assert.Equal(1, game.Board[1].Owner);
        Assert.Equal(0, game.Board[1].Towns);
        Assert.Contains(report.BySeat[0], l => l == "Build 2 towns: not done. You can only build in a region you hold.");
        Assert.Equal(gold + Income(game, 0), game.Seats[0].Gold);
    }

    [Fact]
    public void Building_and_recruiting_cost_gold()
    {
        var game = NewGame(Small);
        var home = game.Board.Homes[0];

        game.PlayRound(Orders(new BuildOrder(home, BuildingType.Node, 2), new RecruitOrder("unit-1", 10)), new SeededRandom(0));

        Assert.Equal(3, game.Board[home].Nodes);
        Assert.Equal(10, game.Seats[0].Army["unit-1"]);
        var spent = (2 * Small.BuildingGoldCost) + (10 * 60);
        Assert.Equal(Small.StartingGold - spent + Income(game, 0) - (10 * Small.UpkeepPerRound), game.Seats[0].Gold);
    }

    [Fact]
    public void Orders_past_the_rounds_actions_are_not_done()
    {
        var game = NewGame(Six);
        var home = game.Board.Homes[0];

        var report = game.PlayRound(
            Orders(new ClaimOrder(1), new ClaimOrder(5), new BuildOrder(home, BuildingType.Town, 1), new RecruitOrder("unit-1", 1)),
            new SeededRandom(0));

        Assert.Equal("Recruit 1 Unit 1: not done. You had no actions left.", report.BySeat[0][0]);
        Assert.Empty(game.Seats[0].Army);
        Assert.NotNull(game.CheckOrders(0, [new ClaimOrder(1), new ClaimOrder(5), new BuildOrder(home, BuildingType.Town, 1), new RecruitOrder("unit-1", 1)]));
    }

    [Fact]
    public void Checking_orders_counts_their_gold_together()
    {
        var game = NewGame(Small with { StartingGold = 800 });
        var home = game.Board.Homes[0];

        Assert.Null(game.CheckOrders(0, [new BuildOrder(home, BuildingType.Town, 2)])); // 600
        Assert.Equal(
            "It costs 600 gold, but you'd have 500.",
            game.CheckOrders(0, [new BuildOrder(home, BuildingType.Town, 1), new BuildOrder(home, BuildingType.Node, 2)]));
    }

    [Fact]
    public void Orders_the_check_accepts_play_out_when_nobody_gets_in_the_way()
    {
        var checkedOrders = 0;
        for (var seed = 0; seed < 400; seed++)
        {
            var game = SkirmishGame.Start([("A", MagicSchool.A), ("B", MagicSchool.B), ("C", MagicSchool.C)], SkirmishRules.Default, new SeededRandom(seed));
            var orders = RandomOrders(game, 0, new Random(seed));
            if (game.CheckOrders(0, orders) is not null)
            {
                continue;
            }

            var report = game.PlayRound(new Dictionary<int, IReadOnlyList<SkirmishOrder>> { [0] = orders }, new SeededRandom(seed));

            Assert.DoesNotContain(report.BySeat[0], l => l.Contains("not done", StringComparison.Ordinal));
            checkedOrders++;
        }

        Assert.True(checkedOrders > 50, $"only {checkedOrders} order sets passed the check");
    }

    [Fact]
    public void Income_comes_from_regions_towns_and_nodes_then_upkeep_is_paid()
    {
        var game = NewGame(Small);

        var report = game.PlayRound(Orders(), new SeededRandom(0));

        // Home: 100 for the region, 60 for its town; 50 mana for its node.
        Assert.Equal("Your regions brought in 160 gold and 50 mana.", report.BySeat[0].Single());
        Assert.Equal(Small.StartingGold + 160, game.Seats[0].Gold);
        Assert.Equal(Small.StartingMana + 50, game.Seats[0].Mana);
    }

    [Fact]
    public void Unpaid_upkeep_costs_all_gold_and_some_units()
    {
        var game = NewGame(Small with { StartingGold = 1_200, UpkeepPerRound = 100 });
        game.PlayRound(Orders(new RecruitOrder("unit-1", 20)), new SeededRandom(0)); // 1,200 gold; upkeep 2,000

        Assert.Equal(0, game.Seats[0].Gold);
        Assert.Equal(18, game.Seats[0].Army["unit-1"]);
    }

    [Fact]
    public void Cards_do_what_they_say()
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var game = NewGame(Small, seed);
            game.PlayRound(Orders(new RecruitOrder("unit-1", 20)), new SeededRandom(0));
            var card = game.Deck[0];
            var (gold, mana, units) = (game.Seats[0].Gold, game.Seats[0].Mana, game.Seats[0].Units);

            game.PlayRound(Orders(new ClaimOrder(1)), new SeededRandom(0));

            var income = Income(game, 0);
            var seat = game.Seats[0];
            var upkeep = seat.Units; // a Card 5 loss comes before upkeep
            Assert.Equal(card == DiscoveryCard.Card2 ? gold + 600 + income - upkeep : gold + income - upkeep, seat.Gold);
            Assert.Equal(card == DiscoveryCard.Card3 ? mana + 400 + 50 : mana + 50, seat.Mana);
            Assert.Equal(card == DiscoveryCard.Card4 ? 3 : 0, seat.CardPoints);
            Assert.Equal(card == DiscoveryCard.Card5 ? units - 2 : units, seat.Units);
        }
    }

    [Fact]
    public void The_game_ends_after_its_rounds_and_the_most_points_win()
    {
        var game = NewGame(Six with { Rounds = 2 });

        game.PlayRound(Orders(new ClaimOrder(1)), new SeededRandom(0));
        Assert.False(game.Ended);
        var last = game.PlayRound(Orders(), new SeededRandom(0));

        Assert.True(game.Ended);
        Assert.Equal("Merlin", game.Standings()[0].Name);
        Assert.StartsWith("The last round is over. Merlin wins with", last.Public[^1], StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => game.PlayRound(Orders(), new SeededRandom(0)));
        Assert.Equal("The game is over.", game.CheckOrders(0, []));
    }

    [Fact]
    public void Holding_half_the_board_ends_the_game_early()
    {
        var game = NewGame(Small);

        var report = game.PlayRound(Orders(new ClaimOrder(1)), new SeededRandom(0));

        Assert.True(game.Ended);
        Assert.StartsWith("Merlin holds 50% of the board: the game ends.", report.Public[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Scores_count_regions_rich_terrain_buildings_sets_and_cards()
    {
        var game = NewGame(SkirmishRules.Default);
        var home = game.Board.Homes[0];

        var score = game.Score(0);

        Assert.Equal(new SkirmishScore(3, 0, 4, 0, 0, 0, 0), score); // one T1 region with a node and a town
        Assert.Equal(7, score.Total);
        Assert.Equal(SkirmishTerrain.T1, game.Board[home].Terrain);
    }

    [Fact]
    public void The_same_seed_and_orders_replay_exactly()
    {
        string Play()
        {
            var game = SkirmishGame.Start([("A", MagicSchool.A), ("B", MagicSchool.C), ("C", MagicSchool.E)], SkirmishRules.Default, new SeededRandom(9));
            var random = new SeededRandom(10);
            var lines = new List<string>();
            var choices = new Random(11);
            while (!game.Ended)
            {
                var orders = game.Seats.ToDictionary(s => s.Index, s => (IReadOnlyList<SkirmishOrder>)RandomOrders(game, s.Index, choices));
                var report = game.PlayRound(orders, random);
                lines.AddRange(report.Public);
                lines.AddRange(report.BySeat.SelectMany(l => l));
            }

            return string.Join('\n', lines) + Snapshot(game);
        }

        Assert.Equal(Play(), Play());
    }

    /// <summary>A few plausible orders for the seat: claims, builds, recruits, attacks, spells and hexes.</summary>
    private static List<SkirmishOrder> RandomOrders(SkirmishGame game, int seat, Random random)
    {
        var orders = new List<SkirmishOrder>();
        var claimable = game.Claimable(seat).ToList();
        var attackable = game.Board.Regions
            .Where(r => r.Owner is { } o && o != seat && r.Neighbours.Any(n => game.Board[n].Owner == seat)).ToList();
        var school = game.Seats[seat].School;
        var others = game.Seats.Where(s => s.Index != seat && !s.Out).ToList();
        for (var i = 0; i < 3; i++)
        {
            orders.Add(random.Next(6) switch
            {
                0 when claimable.Count > 0 => new ClaimOrder(claimable[random.Next(claimable.Count)].Id),
                1 when game.RegionsOf(seat).Any() => new BuildOrder(game.RegionsOf(seat).First().Id, random.Next(2) == 0 ? BuildingType.Node : BuildingType.Town, 1 + random.Next(2)),
                2 when attackable.Count > 0 => new AttackOrder(attackable[random.Next(attackable.Count)].Id),
                3 => new CastOrder(random.Next(2) == 0 ? "ward" : SpellBook.ForSchool(school).First(sp => sp.School == school).Id),
                4 when others.Count > 0 => new HexOrder(SpellBook.HexesForSchool(school).First().Id, others[random.Next(others.Count)].Index),
                _ => new RecruitOrder(UnitBook.All[random.Next(3)].Id, 1 + random.Next(20)),
            });
        }

        return orders;
    }

    /// <summary>What the seat's regions bring in this round (no terrain bonuses on the small boards' home).</summary>
    private static int Income(SkirmishGame game, int seat) =>
        game.RegionsOf(seat).Sum(r => game.Rules.GoldPerRegion + (int)(r.Towns * game.Rules.GoldPerTown * (r.Terrain == SkirmishTerrain.T2 ? game.Rules.Terrain2TownGold : 1)));

    private static string Snapshot(SkirmishGame game) =>
        string.Join(';', game.Board.Regions.Select(r => $"{r.Owner}/{r.Nodes}/{r.Towns}"))
        + "|" + string.Join(';', game.Seats.Select(s => $"{s.Gold}/{s.Mana}/{s.Units}/{s.CardPoints}/{s.BattlesWon}/{s.ActionBonus}/{s.Effects.Count}/{s.Out}"))
        + "|" + game.Deck.Count;

    private sealed class FixedRandom(double value) : IRandomSource
    {
        public double NextDouble() => value;
    }

    [Fact]
    public void A_creator_chooses_switches_within_their_limits()
    {
        Assert.Null(SkirmishRules.Default.CreatorProblem());
        Assert.Null((SkirmishRules.Default with { Players = 2, Rounds = 24, ActionsPerRound = 5 }).CreatorProblem());
        Assert.Equal("A Skirmish has 2–6 players.", (SkirmishRules.Default with { Players = 1 }).CreatorProblem());
        Assert.Equal("A Skirmish lasts 4–24 rounds.", (SkirmishRules.Default with { Rounds = 3 }).CreatorProblem());
        Assert.Equal("Players get 2–5 actions a round.", (SkirmishRules.Default with { ActionsPerRound = 6 }).CreatorProblem());
    }
}
