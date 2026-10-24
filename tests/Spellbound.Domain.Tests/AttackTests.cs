namespace Spellbound.Domain.Tests;

public class AttackTests
{
    private static Match Opened(int seed, GameRules? rules = null, int players = 3)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(players, rules ?? GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Draft(new Pick(match.Picker!.Value, match.ToPick[0]), random);
        }

        return match;
    }

    /// <summary>The first seeded match where the condition holds, so tests don't depend on one seed's layout.</summary>
    private static Match FirstWhere(Func<Match, bool> condition, GameRules? rules = null, int players = 3) =>
        Opened(Enumerable.Range(1, 2000).First(seed => condition(Opened(seed, rules, players))), rules, players);

    /// <summary>Plays a round: these orders, and every other seat holds.</summary>
    private static Report Play(Match match, params Orders[] orders)
    {
        var report = match.Resolve(
            [.. orders, .. match.Seats.Where(s => orders.All(o => o.Seat != s.Index)).Select(s => Orders.Hold(s.Index))],
            new SeededRandom(match.CurrentRound));
        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        return report;
    }

    /// <summary>A tile of <paramref name="seat"/> next to a tile of <paramref name="rival"/>, if any.</summary>
    private static (Hex Mine, Hex Theirs)? Border(Match match, int seat, int rival) =>
        match.Seats[seat].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R)
            .SelectMany(a => match.Seats[rival].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).Where(a.IsNextTo).Select(b => (a, b)))
            .Cast<(Hex, Hex)?>().FirstOrDefault();

    private static Orders Attack(int seat, Hex from, Hex target, Army army) =>
        Orders.Attack(seat, target, [new Move(from, target, army)]);

    private static string Snapshot(Match match) =>
        $"{match.Phase} {match.CurrentRound} "
        + string.Join(" | ", match.Seats.Select(s => $"{s.Mana} {s.Score}: "
            + string.Join(' ', s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).Select(h => $"{h}{match.UnitsOn(h)}"))))
        + " / " + string.Join(' ', match.Map.Tiles.Select(t => $"{t.Yield}{t.Guard}"));

    [Theory]
    [InlineData(4, 0, "takes", 1)]
    [InlineData(3, 1, "tie", 0)]
    [InlineData(2, 1, "fails", 0)]
    public void An_attack_on_a_rivals_tile_follows_the_battle_rules(int imps, int ownerAfter, string result, int impsLeft)
    {
        var match = FirstWhere(m => Border(m, 0, 1) is not null);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, imps), Orders.Summon(1, theirs, UnitKind.Golem, 1));

        var report = Play(match, Attack(0, mine, theirs, new Army(imps, 0)));

        Assert.Contains(report.Lines, l => l.Contains(result, StringComparison.Ordinal));
        Assert.Equal(ownerAfter, match.OwnerOf(theirs));
        Assert.Equal(Army.None, match.UnitsOn(mine));
        Assert.Equal(ownerAfter == 0 ? new Army(impsLeft, 0) : result == "fails" ? new Army(0, 1) : Army.None, match.UnitsOn(theirs));
        Assert.Equal(ownerAfter == 0, report.Lines.Any(l => l.Contains("2 for conquest", StringComparison.Ordinal)));
    }

    [Fact]
    public void Taking_an_undefended_rival_tile_scores_less_than_a_defended_one()
    {
        // A rival with at least two tiles, so the one taken isn't its last.
        var match = FirstWhere(m => Border(m, 0, 1) is not null && m.Seats[1].Tiles.Count > 1);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 2));

        var report = Play(match, Attack(0, mine, theirs, new Army(2, 0)));

        Assert.Equal(0, match.OwnerOf(theirs));
        Assert.Contains(report.Lines, l => l == $"Seat 1 takes {theirs} with 2 Imps, undefended.");
        Assert.Contains(report.Lines, l => l.StartsWith("Seat 1 banks", StringComparison.Ordinal) && l.Contains("1 for conquest", StringComparison.Ordinal));
    }

    [Fact]
    public void An_attack_can_bring_reinforcements_that_guard_home_but_dont_attack()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is not null && m.Seats[1].Tiles.Count > 1);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 2));
        var mana = match.Seats[0].Mana;
        var orders = Attack(0, mine, theirs, new Army(2, 0)).Reinforcing(mine, UnitKind.Imp, 3);

        var check = match.Check(orders);
        var report = Play(match, orders);

        Assert.Contains(check.Lines, l => l.StartsWith("Reinforce", StringComparison.Ordinal) && l.Contains("for 6 mana", StringComparison.Ordinal));
        // The preview says what it can't know: rivals' secret orders this round.
        Assert.Contains(check.Lines, l => l.Contains("rivals give orders in secret too", StringComparison.Ordinal));
        Assert.Contains(report.Lines, l => l == $"Seat 1 reinforces {mine} with 3 Imps.");
        Assert.Equal(new Army(3, 0), match.UnitsOn(mine)); // they stayed home
        Assert.Equal(new Army(2, 0), match.UnitsOn(theirs)); // the attack went on with the 2 sent
        Assert.Equal(mana - 6 + match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value), match.Seats[0].Mana);
    }

    [Theory]
    [InlineData("without an attack", "come with an attack")]
    [InlineData("too many", "at most 3 power")]
    [InlineData("on a rival's tile", "only be summoned on your own tiles")]
    public void Reinforcements_that_dont_check_change_nothing(string problem, string reason)
    {
        var match = FirstWhere(m => Border(m, 0, 1) is not null);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 2));
        var attack = Attack(0, mine, theirs, new Army(2, 0));
        var orders = problem switch
        {
            "without an attack" => Orders.Hold(0).Reinforcing(mine, UnitKind.Imp, 1),
            "too many" => attack.Reinforcing(mine, UnitKind.Imp, 4),
            _ => attack.Reinforcing(theirs, UnitKind.Imp, 1),
        };
        var before = Snapshot(match);

        var check = match.Check(orders);
        var report = match.Resolve([orders, Orders.Hold(1), Orders.Hold(2)], new SeededRandom(1));

        Assert.False(check.Succeeded);
        Assert.Contains(reason, check.Lines[0], StringComparison.Ordinal);
        Assert.False(report.Succeeded);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Units_that_attack_dont_defend_their_own_tile()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is not null);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 4), Orders.Summon(1, theirs, UnitKind.Golem, 1));

        Play(match, Attack(0, mine, theirs, new Army(4, 0)), Attack(1, theirs, mine, new Army(0, 1)));

        // Each side's tile was empty when the other arrived, so they swap tiles.
        Assert.Equal(1, match.OwnerOf(mine));
        Assert.Equal(new Army(0, 1), match.UnitsOn(mine));
        Assert.Equal(0, match.OwnerOf(theirs));
        Assert.Equal(new Army(4, 0), match.UnitsOn(theirs));
    }

    [Fact]
    public void A_rivals_last_tile_cant_be_taken()
    {
        var rules = GameRules.Default with { OpeningUnveils = 1 };
        var match = FirstWhere(m => Border(m, 0, 1) is not null, rules);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 4), Orders.Summon(1, theirs, UnitKind.Imp, 1));

        var report = Play(match, Attack(0, mine, theirs, new Army(4, 0)));

        Assert.Equal(1, match.OwnerOf(theirs));
        Assert.Equal(Army.None, match.UnitsOn(theirs));
        Assert.Equal(new Army(3, 0), match.UnitsOn(mine));
        Assert.Contains(report.Lines, l => l.Contains("last tile", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Lines, l => l.Contains("for conquest", StringComparison.Ordinal));
    }

    [Fact]
    public void Several_attackers_fight_the_defender_together_then_the_strongest_survivor_takes_the_tile()
    {
        var match = FirstWhere(m => m.Seats[1].Tiles.Any(t => m.Seats[0].Tiles.Any(t.IsNextTo) && m.Seats[2].Tiles.Any(t.IsNextTo)));
        var target = match.Seats[1].Tiles.First(t => match.Seats[0].Tiles.Any(t.IsNextTo) && match.Seats[2].Tiles.Any(t.IsNextTo));
        var from0 = match.Seats[0].Tiles.First(target.IsNextTo);
        var from2 = match.Seats[2].Tiles.First(target.IsNextTo);
        Play(match, Orders.Summon(0, from0, UnitKind.Imp, 5), Orders.Summon(1, target, UnitKind.Imp, 1), Orders.Summon(2, from2, UnitKind.Imp, 3));

        var report = Play(match, Attack(0, from0, target, new Army(5, 0)), Attack(2, from2, target, new Army(3, 0)));

        // 8 against 1: Seat 1 loses its Imp's worth from the weakest (Seat 1 first in seat order), leaving 4 and 3.
        // Then 4 beats 3 and keeps 1.
        Assert.Contains(report.Lines, l => l.Contains("fight each other", StringComparison.Ordinal));
        Assert.Equal(0, match.OwnerOf(target));
        Assert.Equal(new Army(1, 0), match.UnitsOn(target));
    }

    [Fact]
    public void A_tie_between_the_strongest_survivors_leaves_the_tile_with_the_defender()
    {
        var match = FirstWhere(m => m.Seats[1].Tiles.Any(t => m.Seats[0].Tiles.Any(t.IsNextTo) && m.Seats[2].Tiles.Any(t.IsNextTo)));
        var target = match.Seats[1].Tiles.First(t => match.Seats[0].Tiles.Any(t.IsNextTo) && match.Seats[2].Tiles.Any(t.IsNextTo));
        var from0 = match.Seats[0].Tiles.First(target.IsNextTo);
        var from2 = match.Seats[2].Tiles.First(target.IsNextTo);
        Play(match, Orders.Summon(0, from0, UnitKind.Imp, 3), Orders.Summon(2, from2, UnitKind.Imp, 3));

        Play(match, Attack(0, from0, target, new Army(3, 0)), Attack(2, from2, target, new Army(3, 0)));

        Assert.Equal(1, match.OwnerOf(target));
        Assert.Equal(Army.None, match.UnitsOn(target));
    }

    /// <summary>A match in round 2, after the first later Unveil, where every revealed tile is guarded and one is next
    /// to seat 0's land.</summary>
    private static (Match Match, Hex Guarded, Hex From) GuardedNextToSeat0()
    {
        var rules = GameRules.Default with { GuardedFaces = 6 };
        Hex? Guarded(Match m) => m.Map.Tiles.Where(t => t.IsGuarded && m.Seats[0].Tiles.Any(t.Hex.IsNextTo))
            .Select(t => t.Hex).OrderBy(h => h.Q).ThenBy(h => h.R).Cast<Hex?>().FirstOrDefault();
        var match = FirstWhere(m => { Play(m); return Guarded(m) is not null; }, rules);
        Play(match);
        var guarded = Guarded(match)!.Value;
        return (match, guarded, match.Seats[0].Tiles.First(guarded.IsNextTo));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(3, 0)]
    public void Monster_guards_are_cleared_by_attacking_and_a_tie_gives_the_attacker_the_tile(int imps, int left)
    {
        var (match, guarded, from) = GuardedNextToSeat0();
        Assert.Equal(3, match.Map[guarded].Guard);
        Play(match, Orders.Summon(0, from, UnitKind.Imp, imps));

        Play(match, Attack(0, from, guarded, new Army(imps, 0)));

        Assert.Equal(0, match.OwnerOf(guarded));
        Assert.False(match.Map[guarded].IsGuarded);
        Assert.Equal(new Army(left, 0), match.UnitsOn(guarded));
    }

    [Fact]
    public void A_guarded_tile_cant_be_attuned_and_a_weaker_attack_leaves_the_guards()
    {
        var (match, guarded, from) = GuardedNextToSeat0();
        Assert.Contains("guarded by monsters", Assert.Single(match.Check(Orders.Attune(0, guarded)).Lines), StringComparison.Ordinal);
        Play(match, Orders.Summon(0, from, UnitKind.Imp, 2));

        Play(match, Attack(0, from, guarded, new Army(2, 0)));

        Assert.Null(match.OwnerOf(guarded));
        Assert.Equal(3, match.Map[guarded].Guard);
        Assert.Equal(Army.None, match.UnitsOn(from));
    }

    [Theory]
    [InlineData(3, 7, 4, 3)]
    [InlineData(5, 19, 10, 9)]
    public void The_later_Unveils_reveal_the_dark_tiles_split_over_rounds_2_and_4(int players, int dark, int round2, int round4)
    {
        var match = Opened(1, players: players);
        Assert.Equal(dark, match.Map.Dark.Count());

        Play(match);
        Assert.Equal(dark - round2, match.Map.Dark.Count());
        Play(match);
        Assert.Equal(round4, match.Map.Dark.Count());
        Play(match);
        Assert.Empty(match.Map.Dark);
    }

    [Fact]
    public void Check_projects_the_battle_from_what_both_sides_have_now()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is not null);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 5), Orders.Summon(1, theirs, UnitKind.Golem, 1));

        var line = Assert.Single(match.Check(Attack(0, mine, theirs, new Army(5, 0))).Lines);

        Assert.Contains("power 3", line, StringComparison.Ordinal);
        Assert.Contains("power 5", line, StringComparison.Ordinal);
        Assert.Contains("keeping 2 Imps", line, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> BadAttacks => new()
    {
        { "own tile", "already yours" },
        { "free tile", "attuned, not attacked" },
        { "no units", "needs units" },
        { "too many units", "There aren't 9 Imps" },
        { "from afar", "from your tiles next to" },
    };

    [Theory]
    [MemberData(nameof(BadAttacks))]
    public void An_attack_that_doesnt_check_changes_nothing(string problem, string reason)
    {
        var match = FirstWhere(m => Border(m, 0, 1) is not null
            && m.Map.Tiles.Any(t => t.IsUnveiled && m.OwnerOf(t.Hex) is null)
            && m.Seats[0].Tiles.Any(h => !Border(m, 0, 1)!.Value.Theirs.IsNextTo(h)));
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        var free = match.Map.Tiles.First(t => t.IsUnveiled && match.OwnerOf(t.Hex) is null).Hex;
        var far = match.Seats[0].Tiles.First(h => !theirs.IsNextTo(h));
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 2), Orders.Summon(0 + 1, theirs, UnitKind.Imp, 1));
        var orders = problem switch
        {
            "own tile" => Attack(0, far, mine, new Army(1, 0)),
            "free tile" => Attack(0, mine, free, new Army(1, 0)),
            "no units" => Orders.Attack(0, theirs, []),
            "too many units" => Attack(0, mine, theirs, new Army(9, 0)),
            _ => Attack(0, far, theirs, new Army(1, 0)),
        };
        var before = Snapshot(match);

        var report = match.Resolve([orders, Orders.Hold(1), Orders.Hold(2)], new SeededRandom(1));

        Assert.False(report.Succeeded);
        Assert.Contains(reason, Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void A_bot_game_with_attacks_always_plays_out_the_same()
    {
        var first = PlayAggressively(12);

        Assert.Equal(first, PlayAggressively(12));
        Assert.Contains("attack", first, StringComparison.Ordinal);
        Assert.Contains("The match is over", first, StringComparison.Ordinal);
    }

    /// <summary>A whole match between bots, from the opening to the end.</summary>
    private static string PlayAggressively(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        var lines = new List<string>();
        while (match.Phase != MatchPhase.Ended)
        {
            var report = match.Phase == MatchPhase.Opening
                ? match.Draft(Bot.ChoosePick(match, match.Picker!.Value, random)!, random)
                : match.Resolve([.. match.Seats.Select(s => Bot.ChooseOrders(match, s.Index, random))], random);
            Assert.True(report.Succeeded, string.Join('\n', report.Lines));
            lines.AddRange(report.Lines);
        }

        return string.Join('\n', lines);
    }
}
