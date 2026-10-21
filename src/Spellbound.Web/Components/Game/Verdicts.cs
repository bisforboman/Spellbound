namespace Spellbound.Web.Components.Game;

/// <summary>A battle's verdict for the player, branded onto the report as a stamp (docs/theme.md).</summary>
public sealed record Verdict(string Text, bool Won);

/// <summary>
/// Finds the battles the player fought in a round's report and their verdict for them. A battle is the line where the
/// attackers meet the defence ("… attack …: power A against D.") and the lines that settle it; the verdict goes after
/// the last of those. Battles between others get no stamp.
/// </summary>
public static class Verdicts
{
    private static readonly string[] Settling =
        ["The attack fails", "A tie", "The attackers", "The strongest", "It's Seat "];

    /// <summary>The verdicts by the index of the report line they follow.</summary>
    public static IReadOnlyDictionary<int, Verdict> Find(IReadOnlyList<string> lines, int you)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var me = $"Seat {you + 1}";
        var verdicts = new Dictionary<int, Verdict>();
        for (var start = 0; start < lines.Count; start++)
        {
            if (!IsBattle(lines[start]))
            {
                continue;
            }

            var end = start;
            while (end + 1 < lines.Count && IsSettling(lines[end + 1]))
            {
                end++;
            }

            var opening = lines[start];
            var attacked = opening.StartsWith($"{me} (", StringComparison.Ordinal) || opening.Contains($" and {me} (", StringComparison.Ordinal);
            var defended = opening.Contains($"{me}'s, held by", StringComparison.Ordinal);
            var settled = lines.Skip(start + 1).Take(end - start).ToList();
            var taker = settled.FirstOrDefault(l => l.StartsWith("Seat ", StringComparison.Ordinal) && l.Contains(" takes ", StringComparison.Ordinal));
            Verdict? verdict = (attacked, defended) switch
            {
                (true, _) when taker?.StartsWith($"{me} takes", StringComparison.Ordinal) == true => new("Conquered", true),
                (true, _) when settled.Any(l => l.StartsWith("The attack fails", StringComparison.Ordinal)) => new("Repelled", false),
                (true, _) => new("Thwarted", false),
                (_, true) when taker is not null => new("Lost", false),
                (_, true) => new("Held", true),
                _ => null,
            };
            if (verdict is not null)
            {
                verdicts[end] = verdict;
            }

            start = end;
        }

        return verdicts;
    }

    /// <summary>
    /// The report lines that are about the player, for the short report: their own lines, the battles they fought
    /// (every line of them), and the lines that mark where the match is (a round, an Unveil, the start of the rounds,
    /// the end). By line index, so the stamps still land.
    /// </summary>
    public static IReadOnlySet<int> Yours(IReadOnlyList<string> lines, int you)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var me = new System.Text.RegularExpressions.Regex($@"\bSeat {you + 1}\b");
        var yours = new HashSet<int>();
        var verdicts = Find(lines, you);
        for (var i = 0; i < lines.Count; i++)
        {
            if (me.IsMatch(lines[i]) || Markers.Any(m => lines[i].StartsWith(m, StringComparison.Ordinal)))
            {
                yours.Add(i);
            }

            if (IsBattle(lines[i]))
            {
                var end = i;
                while (end + 1 < lines.Count && IsSettling(lines[end + 1]))
                {
                    end++;
                }

                if (verdicts.ContainsKey(end))
                {
                    yours.UnionWith(Enumerable.Range(i, end - i + 1));
                }

                i = end;
            }
        }

        return yours;
    }

    private static readonly string[] Markers = ["Round ", "An Unveil", "The opening is over", "The match is over"];

    private static bool IsBattle(string line) =>
        line.Contains(" attack ", StringComparison.Ordinal) && line.Contains(": power ", StringComparison.Ordinal);

    private static bool IsSettling(string line) =>
        !IsBattle(line) && (Settling.Any(s => line.StartsWith(s, StringComparison.Ordinal))
                            || (line.StartsWith("Seat ", StringComparison.Ordinal) && line.Contains(" takes ", StringComparison.Ordinal)));
}
