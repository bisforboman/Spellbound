using Spellbound.Domain;
using Spellbound.Web.Components.Game;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

public class FogStorageTests
{
    private static Match Opened(int seed = 1)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Bidders.Select(Bid.Pass)], random);
        }

        return match;
    }

    [Fact]
    public void A_stored_report_keeps_whom_each_line_concerns_and_the_page_shows_a_player_only_theirs()
    {
        var match = Opened();
        var report = match.Resolve([.. match.Seats.Select(s => Orders.Hold(s.Index))], new SeededRandom(1));
        var stored = MatchJson.Deserialize<StoredMatch>(MatchJson.Serialize(new StoredMatch(1, match.Save(), report.Lines, report.Tags)));
        var loaded = new LoadedMatch(1, ["alice", null, null], Match.Load(stored.Match, match.Rules))
        {
            LastReport = stored.LastReport!, LastReportTags = stored.LastReportTags,
        };

        var seen = loaded.LastReportFor(0);

        Assert.Contains(seen, l => l.StartsWith("Seat 1 collects", StringComparison.Ordinal));
        Assert.DoesNotContain(seen, l => l.StartsWith("Seat 2 collects", StringComparison.Ordinal)); // rivals' mana is hidden
        Assert.Contains(seen, l => l.StartsWith("Seat 2 banks", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rivals_tile_far_from_yours_says_its_units_are_unseen()
    {
        // The first seeded layout with a rival tile away from seat 1's land.
        var match = Enumerable.Range(1, 500).Select(Opened).First(m => m.Seats[1].Tiles.Any(h => !m.CanSee(0, h)));
        var far = match.Seats[1].Tiles.First(h => !match.CanSee(0, h));

        Assert.EndsWith("units unseen", Seats.Describe(match, far, 0), StringComparison.Ordinal);
        Assert.DoesNotContain("unseen", Seats.Describe(match, far, 1), StringComparison.Ordinal);
    }
}
