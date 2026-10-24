using System.Text.Json;

namespace Spellbound.Domain.Tests;

public class SavingTests
{
    private static string Json(MatchState state) => JsonSerializer.Serialize(state);

    /// <summary>Plays a bot match from the start; <paramref name="reload"/> saves and loads it after every step.</summary>
    private static (List<string> Lines, List<string> States) Play(int seed, bool reload)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        var lines = new List<string>();
        var states = new List<string> { Json(match.Save()) };
        while (match.Phase != MatchPhase.Ended)
        {
            var report = match.Phase == MatchPhase.Opening
                ? match.Auction([.. match.Bidders.Select(s => Bot.ChooseBid(match, s, random))], random)
                : match.Resolve([.. match.Seats.Select(s => Bot.ChooseOrders(match, s.Index, random))], random);
            Assert.True(report.Succeeded);
            lines.AddRange(report.Lines);
            if (reload)
            {
                match = Match.Load(match.Save(), GameRules.Default);
            }

            states.Add(Json(match.Save()));
        }

        return (lines, states);
    }

    [Fact]
    public void Saving_and_loading_after_every_step_plays_out_exactly_like_an_unbroken_match()
    {
        var unbroken = Play(21, reload: false);
        var reloaded = Play(21, reload: true);

        Assert.Equal(unbroken.Lines, reloaded.Lines);
        Assert.Equal(unbroken.States, reloaded.States);
    }

    [Fact]
    public void A_loaded_match_saves_exactly_as_it_was_saved_at_every_step()
    {
        // From the dark map through the opening, rounds with units and guarded tiles, to the end.
        var (_, states) = Play(8, reload: false);

        Assert.All(states, json =>
            Assert.Equal(json, Json(Match.Load(JsonSerializer.Deserialize<MatchState>(json)!, GameRules.Default).Save())));
        Assert.Contains(states, s => s.Contains("\"Guard\":3", StringComparison.Ordinal));
        Assert.Contains(states, s => s.Contains("\"Imps\":", StringComparison.Ordinal));
        Assert.Contains("\"Phase\":2", states[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_match_saved_by_an_unknown_version_is_refused()
    {
        var state = Match.Start(2, GameRules.Default, new SeededRandom(1)).Save() with { Version = 99 };

        Assert.Throws<NotSupportedException>(() => Match.Load(state, GameRules.Default));
    }

    [Fact]
    public void A_match_saved_before_land_carried_over_still_loads()
    {
        var json = Json(Match.Start(2, GameRules.Default, new SeededRandom(1)).Save());
        var old = System.Text.RegularExpressions.Regex.Replace(json, ",\"LandCarry\":0", "");

        Assert.DoesNotContain("LandCarry", old, StringComparison.Ordinal);
        var loaded = Match.Load(JsonSerializer.Deserialize<MatchState>(old)!, GameRules.Default);
        Assert.All(loaded.Seats, s => Assert.Equal(0, s.LandCarry));
    }
}
