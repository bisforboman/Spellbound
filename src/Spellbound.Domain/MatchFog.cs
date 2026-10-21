namespace Spellbound.Domain;

/// <summary>The fog of war (docs/game-design.md, Fog of war): a seat sees rivals' units only on tiles next to its own
/// land, and rivals' mana only once the match is over. Who holds each tile, yields, upgrades and scores are public.</summary>
public sealed partial class Match
{
    /// <summary>Whether <paramref name="seat"/> can see the units on a tile: its own, or one next to its land. Units
    /// only ever stand on their owner's tiles, so this is the whole rule. After the match, everything is seen.</summary>
    public bool CanSee(int seat, Hex hex) =>
        Phase == MatchPhase.Ended || seats[seat].Tiles.Contains(hex) || seats[seat].Tiles.Any(hex.IsNextTo);

    /// <summary>The units on a tile as <paramref name="seat"/> sees them; null if the fog hides them.</summary>
    public Army? UnitsSeenBy(int seat, Hex hex) => CanSee(seat, hex) ? UnitsOn(hex) : null;

    /// <summary>A seat's mana as <paramref name="viewer"/> sees it: its own, or anyone's once the match is over.</summary>
    public int? ManaSeenBy(int viewer, int seat) => viewer == seat || Phase == MatchPhase.Ended ? seats[seat].Mana : null;

    /// <summary>The lines of a report that <paramref name="seat"/> may read, as the board stands now: lines for
    /// everyone, its own, and those on a tile it can see. Once the match is over, every line.</summary>
    public Report ReportFor(Report report, int seat)
    {
        ArgumentNullException.ThrowIfNull(report);
        var keep = Enumerable.Range(0, report.Lines.Count).Where(i => Sees(report.TagOf(i), seat)).ToList();
        return report with { Lines = [.. keep.Select(i => report.Lines[i])], Tags = [.. keep.Select(report.TagOf)] };
    }

    /// <summary>Whether a seat may read a line with this tag.</summary>
    public bool Sees(LineTag? tag, int seat) =>
        tag is null || Phase == MatchPhase.Ended || tag.Seat == seat || tag.At.Any(h => CanSee(seat, h));
}
