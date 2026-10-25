namespace Spellbound.Domain;

/// <summary>What an action did, in short lines to show the players. A failed action changed nothing; its lines say
/// why. <see cref="Tags"/>, when given, says for each line whom it concerns, for the fog of war (see
/// <see cref="Match.ReportFor"/>); a line without a tag is for everyone.</summary>
public sealed record Report(bool Succeeded, IReadOnlyList<string> Lines, IReadOnlyList<LineTag?>? Tags = null)
{
    public static Report Fail(string reason) => new(false, [reason]);

    /// <summary>The tag of line <paramref name="index"/>, or null if it is for everyone.</summary>
    public LineTag? TagOf(int index) => Tags is { } tags && index < tags.Count ? tags[index] : null;

    /// <summary>This report followed by <paramref name="next"/>, their tags kept; it succeeded if the last one did.</summary>
    public Report Then(Report next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return new Report(next.Succeeded, [.. Lines, .. next.Lines],
            [.. Lines.Select((_, i) => TagOf(i)), .. next.Lines.Select((_, i) => next.TagOf(i))]);
    }
}

/// <summary>Whom a report line concerns: the seat that did it (who always sees it), and the tiles it happened on
/// (anyone who can see one of them sees it).</summary>
public sealed record LineTag(int? Seat, IReadOnlyList<Hex> At);

/// <summary>Report lines as they are written, each with its tag (none: for everyone).</summary>
internal sealed class ReportLines : List<string>
{
    private readonly List<LineTag?> tags = [];

    public ReportLines()
    {
    }

    public ReportLines(string first) => Add(first);

    public IReadOnlyList<LineTag?> Tags => [.. tags, .. Enumerable.Repeat<LineTag?>(null, Count - tags.Count)];

    /// <summary>A line for the seat that did it and for anyone who can see one of <paramref name="at"/>.</summary>
    public void Add(string line, int? seat, params Hex[] at)
    {
        while (tags.Count < Count)
        {
            tags.Add(null);
        }

        Add(line);
        tags.Add(new LineTag(seat, at));
    }

    /// <summary>Another set of lines, keeping their tags.</summary>
    public void Append(ReportLines other)
    {
        var them = other.Tags;
        for (var i = 0; i < other.Count; i++)
        {
            if (them[i] is { } tag)
            {
                Add(other[i], tag.Seat, [.. tag.At]);
            }
            else
            {
                Add(other[i]);
            }
        }
    }

    public Report ToReport() => new(true, [.. this], Tags);
}
