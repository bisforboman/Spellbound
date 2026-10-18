using Spellbound.Domain;
using Spellbound.Domain.Skirmish;

namespace Spellbound.Domain.Tests;

/// <summary>Part B of Skirmish: attacks, spells, hexes, players who stop playing, and public objectives.</summary>
public sealed class SkirmishConflictTests
{
    /// <summary>
    /// Three seats on a ring of six with no extra links: homes 0, 2 and 4, so region 1 lies between seats 0 and 1 and
    /// region 3 between seats 1 and 2. Plenty of gold, and no early end, so each test sets up what it needs.
    /// </summary>
    private static readonly SkirmishRules Ring = SkirmishRules.Default with
    {
        RegionsPerPlayer = 2,
        ExtraLinks = 0,
        StartingGold = 20_000,
        StartingMana = 5_000,
        BoardShareToWin = 1,
        Discoveries = false,
        AttackRangePercent = 0, // the range and building rules have their own tests
        DefensePerBuilding = 0,
    };

    /// <summary>Schools A, B, C: A is strong against B, B against C.</summary>
    private static SkirmishGame NewGame(SkirmishRules? rules = null) =>
        SkirmishGame.Start([("Merlin", MagicSchool.A), ("Morgana", MagicSchool.B), ("Ada", MagicSchool.C)], rules ?? Ring, new SeededRandom(1));

    private static Dictionary<int, IReadOnlyList<SkirmishOrder>> Orders(int seat, params SkirmishOrder[] orders) => new() { [seat] = orders };

    private static readonly IRandomSource Even = new FixedRandom(0.5); // no luck either way, and a 50% roll

    [Fact]
    public void A_stronger_army_takes_a_region_with_its_buildings()
    {
        var game = NewGame();
        game.PlayRound(Orders(0, new ClaimOrder(1), new RecruitOrder("unit-3", 20)), Even);

        var report = game.PlayRound(Orders(0, new AttackOrder(2)), Even); // Morgana's home: 20 guards, no army

        Assert.Equal(0, game.Board[2].Owner);
        Assert.Equal((1, 1), (game.Board[2].Nodes, game.Board[2].Towns));
        Assert.Equal(1, game.Seats[0].BattlesWon);
        Assert.Equal(19, game.Seats[0].Army["unit-3"]); // 8% of 20, rounded down
        Assert.Contains("Merlin took Region 3 from Morgana.", report.Public);
        Assert.Contains(report.BySeat[1], l => l.StartsWith("Merlin broke through at Region 3", StringComparison.Ordinal));
        Assert.Equal(2, game.Score(0).Battles);
    }

    [Fact]
    public void A_held_attack_takes_nothing()
    {
        var game = NewGame();
        game.PlayRound(Orders(0, new ClaimOrder(1), new RecruitOrder("unit-1", 5)), Even); // attack 5

        var report = game.PlayRound(Orders(0, new AttackOrder(2)), Even);

        Assert.Equal(1, game.Board[2].Owner);
        Assert.Equal(0, game.Seats[0].BattlesWon);
        Assert.Contains("Morgana held Region 3 against Merlin.", report.Public);
    }

    [Fact]
    public void An_attack_that_is_not_possible_spends_the_action_and_changes_nothing_else()
    {
        var game = NewGame();
        var control = NewGame();
        game.PlayRound(Orders(0, new RecruitOrder("unit-3", 10)), Even);
        control.PlayRound(Orders(0, new RecruitOrder("unit-3", 10)), Even);

        var report = game.PlayRound(Orders(0, new AttackOrder(2), new AttackOrder(0)), Even); // not next to us; past the actions
        control.PlayRound(Orders(0), Even);

        Assert.Contains("Attack Region 3: not done. Region 3 isn't next to one of your regions.", report.BySeat[0]);
        Assert.Contains("Attack Region 1: not done. You had no actions left.", report.BySeat[0]);
        Assert.Equal(Snapshot(control), Snapshot(game));

        var unarmed = NewGame();
        unarmed.PlayRound(Orders(0, new ClaimOrder(1)), Even);
        Assert.Equal("You have no army to attack with.", unarmed.CheckOrders(0, [new AttackOrder(2)]));
    }

    [Fact]
    public void When_two_mages_break_one_region_the_stronger_attack_takes_it()
    {
        var game = NewGame();
        game.PlayRound(new Dictionary<int, IReadOnlyList<SkirmishOrder>>
        {
            [0] = [new ClaimOrder(1), new RecruitOrder("unit-3", 20)],
            [2] = [new ClaimOrder(3), new RecruitOrder("unit-3", 10)],
        }, Even);

        var report = game.PlayRound(new Dictionary<int, IReadOnlyList<SkirmishOrder>>
        {
            [0] = [new AttackOrder(2)],
            [2] = [new AttackOrder(2)],
        }, Even);

        Assert.Equal(0, game.Board[2].Owner);
        Assert.Contains(report.BySeat[2], l => l == "Merlin's attack on Region 3 was stronger: they took it.");
        Assert.Equal(0, game.Seats[2].BattlesWon);
    }

    [Fact]
    public void Terrain_and_the_school_ring_change_the_odds_shown_before_attacking()
    {
        var game = NewGame();

        Assert.Equal(20, game.RegionDefense(2)); // Terrain 1, no army
        game.PlayRound(Orders(0, new RecruitOrder("unit-3", 10)), Even);
        Assert.Equal(60 * 1.15, game.AttackStrength(0, 1), 3); // A is strong against B
        Assert.Equal(60, game.AttackStrength(0, 2), 3); // A and C: neither has the edge
    }

    [Fact]
    public void The_chance_an_attack_wins_is_shown_before_attacking()
    {
        var game = NewGame();
        game.PlayRound(Orders(0, new ClaimOrder(1), new RecruitOrder("unit-3", 3)), Even); // attack 18 × 1.15 against 20

        var chance = game.AttackChance(0, 2);

        // Close: 20.7 against 20, both ±10%. Check the sum against rolling it many times.
        var dice = new Random(1);
        var wins = Enumerable.Range(0, 200_000).Count(_ => 20.7 * (0.9 + (dice.NextDouble() * 0.2)) > 20 * (0.9 + (dice.NextDouble() * 0.2)));
        Assert.Equal(wins / 200_000.0, chance, 2);
        Assert.Equal(0, game.AttackChance(0, 1)); // our own region
        game.PlayRound(Orders(0, new RecruitOrder("unit-3", 20)), Even);
        Assert.Equal(1, game.AttackChance(0, 2), 3);
    }

    [Fact]
    public void A_mage_who_stops_readying_up_is_away_then_out()
    {
        var rules = Ring with { AwayAfterMissed = 2, OutAfterMissed = 4 };
        var game = NewGame(rules);
        var ready = new HashSet<int> { 0, 2 };

        game.PlayRound(Orders(0), Even, ready);
        Assert.Equal(20, game.RegionDefense(2));
        var away = game.PlayRound(Orders(0), Even, ready);

        Assert.Contains("Morgana is away: their regions defend at 50% until they play again.", away.Public);
        Assert.Equal(10, game.RegionDefense(2));

        game.PlayRound(Orders(0), Even, ready);
        var gone = game.PlayRound(Orders(0), Even, ready);

        Assert.True(game.Seats[1].Out);
        Assert.Null(game.Board[2].Owner);
        Assert.Equal(SkirmishScore.None, game.Score(1));
        Assert.Contains(gone.Public, l => l.StartsWith("Morgana hasn't played for 4 rounds and is out", StringComparison.Ordinal));
        Assert.Equal("You're out of this game.", game.CheckOrders(1, []));
        Assert.Equal("Choose another mage still in the game.", game.CheckOrders(0, [new HexOrder("hex-a", 1)]));
    }

    [Fact]
    public void Playing_again_ends_being_away()
    {
        var game = NewGame();
        var ready = new HashSet<int> { 0, 2 };
        game.PlayRound(Orders(0), Even, ready);
        game.PlayRound(Orders(0), Even, ready);

        game.PlayRound(Orders(0), Even);

        Assert.Equal(0, game.Seats[1].MissedInARow);
        Assert.Equal(20, game.RegionDefense(2));
    }

    [Fact]
    public void A_hex_lands_by_its_odds_and_does_its_harm()
    {
        var game = NewGame();
        var gold = game.Seats[1].Gold;

        Assert.Equal(0.85, game.HexChance(0, 1), 3); // A is strong against B
        var report = game.PlayRound(Orders(0, new HexOrder("hex-a", 1), new RecruitOrder("unit-1", 1)), Even);

        Assert.Equal(5_000 - 500 + 50, game.Seats[0].Mana); // and the home's node brings in 50
        Assert.Contains(report.BySeat[1], l => l.StartsWith("Merlin cast Hex A at you", StringComparison.Ordinal));

        var morgana = NewGame();
        morgana.PlayRound(Orders(1, new HexOrder("hex-b", 2)), Even); // B is strong against C: 85%
        Assert.Equal(20_000 - 3_000, morgana.Seats[2].Gold - Income(morgana, 2));
        Assert.Equal(gold, game.Seats[1].Gold - Income(game, 1));
    }

    [Fact]
    public void A_ward_cast_the_same_round_turns_a_hex_aside()
    {
        var game = NewGame();
        var control = NewGame();

        var warded = game.PlayRound(new Dictionary<int, IReadOnlyList<SkirmishOrder>>
        {
            [0] = [new HexOrder("hex-a", 1)],
            [1] = [new CastOrder("ward")],
        }, Even);
        control.PlayRound(Orders(0, new HexOrder("hex-a", 1)), Even);

        Assert.Contains(warded.BySeat[0], l => l.EndsWith("but it was turned aside.", StringComparison.Ordinal)); // 85% × 0.5 < roll 0.5
        Assert.Equal(0.425, game.HexChance(0, 1), 3);
        Assert.Equal(5_000 - 500 + 50, game.Seats[0].Mana); // the mana is spent either way
        Assert.Equal(0.85, control.HexChance(0, 1), 3);
    }

    [Fact]
    public void Hex_E_takes_an_action_and_self_spell_E_gives_one()
    {
        var game = SkirmishGame.Start([("Merlin", MagicSchool.E), ("Morgana", MagicSchool.A), ("Ada", MagicSchool.B)], Ring, new SeededRandom(1));

        game.PlayRound(Orders(0, new HexOrder("hex-e", 1), new CastOrder("self-e")), new FixedRandom(0)); // lands

        Assert.Equal(4, game.ActionsFor(0));
        Assert.Equal(2, game.ActionsFor(1));
        Assert.NotNull(game.CheckOrders(1, [new RecruitOrder("unit-1", 1), new RecruitOrder("unit-1", 1), new RecruitOrder("unit-1", 1)]));
        game.PlayRound(Orders(0), Even);
        Assert.Equal(3, game.ActionsFor(0)); // for one round only
        Assert.Equal(3, game.ActionsFor(1));
    }

    [Fact]
    public void A_self_spell_runs_for_its_rounds_then_ends()
    {
        var game = NewGame();
        var plain = NewGame();

        game.PlayRound(Orders(0, new CastOrder("self-a")), Even); // gold income ×1.25
        plain.PlayRound(Orders(0), Even);

        Assert.Equal(game.Seats[0].Gold, plain.Seats[0].Gold + (160 / 4));
        Assert.Single(game.Seats[0].Effects);
        game.PlayRound(Orders(0), Even);
        game.PlayRound(Orders(0), Even);
        Assert.Empty(game.Seats[0].Effects);
    }

    [Fact]
    public void Spells_cost_mana_and_the_check_counts_it_across_orders()
    {
        var game = NewGame(Ring with { StartingMana = 1_000 });

        Assert.Null(game.CheckOrders(0, [new HexOrder("hex-a", 1), new HexOrder("hex-a", 2)]));
        Assert.Equal("It costs 500 mana, but you'd have 0.", game.CheckOrders(0, [new HexOrder("hex-a", 1), new HexOrder("hex-a", 2), new HexOrder("hex-a", 1)]));
        Assert.Equal("You don't know that spell.", game.CheckOrders(0, [new CastOrder("self-b")]));
        Assert.Equal("You don't know that hex.", game.CheckOrders(0, [new HexOrder("hex-b", 1)]));
    }

    [Fact]
    public void Switches_turn_attacks_and_hexes_off()
    {
        var game = NewGame(Ring with { Attacks = false, Hexes = false });

        Assert.Equal("Attacks are off in this game.", game.CheckOrders(0, [new AttackOrder(1)]));
        Assert.Equal("Hexes are off in this game.", game.CheckOrders(0, [new HexOrder("hex-a", 1)]));
    }

    [Fact]
    public void Objectives_go_to_whoever_reaches_them_first()
    {
        var rules = Ring with { Objectives = 6, ObjectiveUnits = 10 };
        var game = NewGame(rules);

        Assert.Equal(6, game.Objectives.Select(o => o.Objective).Distinct().Count());
        var report = game.PlayRound(Orders(0, new RecruitOrder("unit-1", 10)), Even);
        game.PlayRound(Orders(1, new RecruitOrder("unit-1", 10)), Even);

        Assert.Equal([0], game.Objectives.Single(o => o.Objective == SkirmishObjective.Units).ReachedBy);
        Assert.Contains("Merlin reached \"Have 10 units\": +5 points.", report.Public);
        Assert.Equal(5, game.Score(0).Objectives);
        Assert.Equal(0, game.Score(1).Objectives); // reached it a round later: too late
    }

    [Fact]
    public void Buildings_defend_their_region()
    {
        var game = NewGame(Ring with { DefensePerBuilding = 15 });

        Assert.Equal(20 + (2 * 15), game.RegionDefense(2)); // the home's node and town
    }

    [Fact]
    public void A_mage_can_only_attack_mages_not_much_smaller()
    {
        var game = NewGame(Ring with { AttackRangePercent = 60 });
        game.PlayRound(Orders(0, new ClaimOrder(1), new ClaimOrder(5), new RecruitOrder("unit-3", 20)), Even); // 3 regions against 1

        Assert.Equal("Morgana is too small to attack: they need at least 60% of your regions.", game.CheckOrders(0, [new AttackOrder(2)]));
        var report = game.PlayRound(Orders(0, new AttackOrder(2)), Even);
        Assert.Equal(1, game.Board[2].Owner);
        Assert.Contains(report.BySeat[0], l => l.StartsWith("Attack Region 3: not done. Morgana is too small", StringComparison.Ordinal));
    }

    [Fact]
    public void Homes_can_be_made_safe()
    {
        var game = NewGame(Ring with { HomeIsSafe = true });
        game.PlayRound(Orders(0, new ClaimOrder(1), new RecruitOrder("unit-3", 20)), Even);

        Assert.Equal("Region 3 is Morgana's home: homes can't be attacked.", game.CheckOrders(0, [new AttackOrder(2)]));
    }

    [Fact]
    public void Taking_a_region_can_destroy_some_of_its_buildings()
    {
        var game = NewGame(Ring with { CaptureDestroysPercent = 50 });
        game.PlayRound(new Dictionary<int, IReadOnlyList<SkirmishOrder>>
        {
            [0] = [new ClaimOrder(1), new RecruitOrder("unit-3", 20)],
            [1] = [new BuildOrder(2, BuildingType.Town, 2)], // 1 node, 3 towns
        }, Even);

        var report = game.PlayRound(Orders(0, new AttackOrder(2)), Even);

        Assert.Equal(0, game.Board[2].Owner);
        Assert.Equal(2, game.Board[2].Buildings);
        Assert.Contains("Merlin took Region 3 from Morgana; 2 of its buildings were destroyed in the fighting.", report.Public);
    }

    private static int Income(SkirmishGame game, int seat) =>
        game.RegionsOf(seat).Sum(r => game.Rules.GoldPerRegion + (r.Towns * game.Rules.GoldPerTown));

    private static string Snapshot(SkirmishGame game) =>
        string.Join(';', game.Board.Regions.Select(r => $"{r.Owner}/{r.Nodes}/{r.Towns}"))
        + "|" + string.Join(';', game.Seats.Select(s => $"{s.Gold}/{s.Mana}/{s.Units}/{s.BattlesWon}"));

    private sealed class FixedRandom(double value) : IRandomSource
    {
        public double NextDouble() => value;
    }
}
