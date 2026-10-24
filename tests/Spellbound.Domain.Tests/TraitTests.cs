using System.Text.Json;
using static Spellbound.Domain.Tests.Scenarios;

namespace Spellbound.Domain.Tests;

public class TraitTests
{
    private static Match Fresh(int players = 3, int seed = 1) => Match.Start(players, GameRules.Default, new SeededRandom(seed));

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Every_seat_is_offered_its_own_traits_none_shared(int players)
    {
        var match = Fresh(players);

        Assert.All(match.Seats, s => Assert.Equal(GameRules.Default.TraitsOffered, s.Offer.Count));
        var offered = match.Seats.SelectMany(s => s.Offer).ToList();
        Assert.Equal(offered.Count, offered.Distinct().Count());
    }

    [Fact]
    public void The_same_seed_offers_the_same_traits()
    {
        Assert.Equal(
            Fresh(seed: 7).Seats.SelectMany(s => s.Offer),
            Fresh(seed: 7).Seats.SelectMany(s => s.Offer));
    }

    [Fact]
    public void Too_small_a_pool_offers_nothing()
    {
        var match = Match.Start(5, GameRules.Default with { TraitsOffered = 3 }, new SeededRandom(1));

        Assert.All(match.Seats, s => Assert.Empty(s.Offer));
    }

    [Fact]
    public void A_seat_drafts_one_of_its_offer_and_says_what_it_scores()
    {
        var match = Fresh();
        var trait = match.Seats[0].Offer[1];

        var report = match.ChooseTrait(0, trait);

        Assert.True(report.Succeeded);
        Assert.Equal(trait, match.Seats[0].Trait);
        Assert.Contains("up to 3", report.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_trait_not_offered_a_second_pick_or_a_pick_after_the_opening_changes_nothing()
    {
        var match = Fresh();
        var notMine = match.Seats[1].Offer[0];
        var before = Json(match);

        Assert.False(match.ChooseTrait(0, notMine).Succeeded);
        Assert.False(match.ChooseTrait(9, match.Seats[0].Offer[0]).Succeeded);
        Assert.Equal(before, Json(match));

        match.ChooseTrait(0, match.Seats[0].Offer[0]);
        var chosen = Json(match);
        Assert.False(match.ChooseTrait(0, match.Seats[0].Offer[1]).Succeeded);
        Assert.Equal(chosen, Json(match));

        var opened = Opened(1);
        var after = Json(opened);
        Assert.False(opened.ChooseTrait(1, opened.Seats[1].Offer[1]).Succeeded);
        Assert.Equal(after, Json(opened));
    }

    [Fact]
    public void A_seat_that_never_chose_gets_the_first_of_its_offer_when_the_rounds_begin()
    {
        var match = Fresh();
        match.ChooseTrait(1, match.Seats[1].Offer[1]);
        var random = new SeededRandom(1);
        Report report = null!;
        while (match.Phase == MatchPhase.Opening)
        {
            report = match.Auction([.. match.Bidders.Select(Bid.Pass)], random);
        }

        Assert.Equal(match.Seats[0].Offer[0], match.Seats[0].Trait);
        Assert.Equal(match.Seats[1].Offer[1], match.Seats[1].Trait);
        Assert.Contains(report.Lines, l => l == "Seat 1 didn't choose a trait, so it takes the first one it was offered.");
        Assert.DoesNotContain(report.Lines, l => Enum.GetNames<Trait>().Any(t => l.Contains(t, StringComparison.Ordinal))); // secret
        Assert.All(match.Seats, s => Assert.Equal(3, s.OpeningTiles.Count));
    }

    private static Match InRounds()
    {
        var match = Fresh();
        var random = new SeededRandom(1);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Bidders.Select(Bid.Pass)], random);
        }

        return match;
    }

    private static Report Hold(Match match, Orders? first = null) =>
        match.Resolve([first ?? Orders.Hold(0), .. match.Seats.Skip(1).Select(s => Orders.Hold(s.Index))], new SeededRandom(match.CurrentRound));

    [Fact]
    public void A_right_guess_at_a_rivals_trait_scores_at_the_end_and_stays_secret_until_then()
    {
        var match = InRounds();
        var right = match.Seats[1].Trait!.Value;
        var wrong = Enum.GetValues<Trait>().First(t => t != right && !match.Seats[0].Offer.Contains(t));

        var check = match.Check(Orders.Hold(0).Guessing(1, right));
        var report = Hold(match, Orders.Hold(0).Guessing(1, right));
        Hold(match, Orders.Hold(0).Guessing(2, wrong));
        Assert.DoesNotContain(report.Lines, l => l.Contains(right.ToString(), StringComparison.Ordinal));
        match = Match.Load(match.Save(), match.Rules); // guesses are saved with the match
        while (match.Phase != MatchPhase.Ended)
        {
            report = Hold(match);
        }

        Assert.Contains(check.Lines, l => l.Contains("1 point at the end if you're right", StringComparison.Ordinal));
        Assert.Equal(1, match.Seats[0].GuessPoints);
        Assert.Contains(report.Lines, l => l.StartsWith($"Seat 1's guesses: Seat 2 {right} (right), Seat 3 {wrong} (wrong): 1 point", StringComparison.Ordinal));
    }

    public static TheoryData<string, string> BadGuesses => new()
    {
        { "yourself", "no rival Seat 1" },
        { "twice", "can't be changed" },
        { "your own offer", "was offered to you" },
    };

    [Theory]
    [MemberData(nameof(BadGuesses))]
    public void A_guess_that_doesnt_check_changes_nothing(string problem, string reason)
    {
        var match = InRounds();
        var other = Enum.GetValues<Trait>().Where(t => !match.Seats[0].Offer.Contains(t)).ToList();
        if (problem == "twice")
        {
            Hold(match, Orders.Hold(0).Guessing(1, other[0]));
        }

        var orders = problem switch
        {
            "yourself" => Orders.Hold(0).Guessing(0, other[0]),
            "twice" => Orders.Hold(0).Guessing(1, other[1]),
            _ => Orders.Hold(0).Guessing(1, match.Seats[0].Offer[0]),
        };
        var before = System.Text.Json.JsonSerializer.Serialize(match.Save());

        var check = match.Check(orders);
        var report = Hold(match, orders);

        Assert.False(check.Succeeded);
        Assert.Contains(reason, check.Lines[0], StringComparison.Ordinal);
        Assert.False(report.Succeeded);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(match.Save()));
    }

    [Fact]
    public void A_goal_is_scored_once_at_the_end_and_capped()
    {
        // Everyone holds the whole match: Warden keeps all 3 opening tiles, 1 point each beyond the first.
        var match = FirstWhere(m => m.Seats.Any(s => s.Trait == Trait.Warden));
        var warden = match.Seats.First(s => s.Trait == Trait.Warden);
        Report report = null!;
        while (match.Phase == MatchPhase.Rounds)
        {
            report = Play(match);
            if (match.Phase == MatchPhase.Rounds)
            {
                Assert.Equal(0, warden.TraitPoints);
            }
        }

        Assert.Equal(2, warden.TraitPoints);
        Assert.Contains(report.Lines, l => l.StartsWith($"Seat {warden.Index + 1}'s trait, Warden: 3 opening tiles held, 2 points", StringComparison.Ordinal));
        Assert.Equal((3, 2), match.TraitGoal(warden));
    }

    [Fact]
    public void Goals_count_what_the_match_tracks_and_stop_at_the_cap()
    {
        Spell[] all = [Spell.Scorch, Spell.Ward, Spell.Leyline, Spell.Blight, Spell.Lure];
        var spellweaver = SeatZero(s => s with { Trait = Trait.Spellweaver, SpellsCast = all, HexesCast = 2, RoundsCast = 5 });
        var steady = SeatZero(s => s with { Trait = Trait.Spellweaver, SpellsCast = all, HexesCast = 2, RoundsCast = 6 });
        var hexer = SeatZero(s => s with { Trait = Trait.Hexer, SpellsCast = all, HexesCast = 3 });

        Assert.Equal((5, 2), spellweaver.TraitGoal(spellweaver.Seats[0])); // 1 per 2 different spells
        Assert.Equal((5, 3), steady.TraitGoal(steady.Seats[0])); // and 1 for casting in 6 rounds
        Assert.Equal((3, 3), hexer.TraitGoal(hexer.Seats[0])); // 1 point per Scorch or Blight
    }

    [Fact]
    public void Weaver_and_Wanderer_count_connected_groups()
    {
        Hex[] tiles = [new(0, 0), new(1, 0), new(1, -1), new(0, -1), new(-2, 2), new(-2, 1)]; // groups of 4 and 2
        var weaver = SeatZero(s => s with { Trait = Trait.Weaver, Tiles = tiles });
        var wanderer = SeatZero(s => s with { Trait = Trait.Wanderer, Tiles = tiles });
        var lone = SeatZero(s => s with { Trait = Trait.Wanderer, Tiles = [new(0, 0), new(1, 0), new(-2, 2)] });

        Assert.Equal((4, 1), weaver.TraitGoal(weaver.Seats[0])); // 1 per 2 tiles beyond the first 2
        Assert.Equal((2, 2), wanderer.TraitGoal(wanderer.Seats[0])); // 1 per group of 2 or more
        Assert.Equal((1, 1), lone.TraitGoal(lone.Seats[0])); // a lone tile isn't a group
    }

    /// <summary>A fresh match whose first seat is changed through its saved state.</summary>
    private static Match SeatZero(Func<SeatState, SeatState> change)
    {
        var state = Fresh().Save();
        var seats = state.Seats.Select((s, i) => i == 0 ? change(s) : s with { Tiles = [] }).ToList();
        return Match.Load(state with { Seats = seats }, GameRules.Default);
    }

    [Fact]
    public void Traits_and_their_counts_survive_saving()
    {
        var state = Opened(3).Save();
        var match = Match.Load(state with { Seats = [state.Seats[0] with { SpellsCast = [Spell.Blight], HexesCast = 1 }, .. state.Seats.Skip(1)] },
            GameRules.Default);
        var json = Json(match);

        var loaded = Match.Load(JsonSerializer.Deserialize<MatchState>(json)!, GameRules.Default);

        Assert.Equal(json, Json(loaded));
        Assert.NotNull(loaded.Seats[2].Trait);
        Assert.Equal(match.Seats[2].Trait, loaded.Seats[2].Trait);
        Assert.Equal(1, loaded.Seats[0].HexesCast);
    }

    [Fact]
    public void A_match_saved_before_traits_still_loads_without_them()
    {
        var json = Json(Fresh());
        var old = System.Text.RegularExpressions.Regex.Replace(
            json, ",\"(Offer|Trait|TraitPoints|OpeningTiles|MonstersCleared|RivalTilesTaken|SpellsCast|HexesCast)\":(\\[[^\\]]*\\]|[^,}]*)", "");

        Assert.DoesNotContain("Offer", old, StringComparison.Ordinal);
        var loaded = Match.Load(JsonSerializer.Deserialize<MatchState>(old)!, GameRules.Default);
        Assert.All(loaded.Seats, s => Assert.Empty(s.Offer));
        Assert.All(loaded.Seats, s => Assert.Null(s.Trait));
    }

    private static string Json(Match match) => JsonSerializer.Serialize(match.Save());
}
