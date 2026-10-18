namespace Spellbound.Domain;

/// <summary>How mages are ranked, for the rivals list and the hall of fame. Ties go to the earlier name.</summary>
public static class Rankings
{
    public static IEnumerable<Mage> ByLand(IEnumerable<Mage> mages) =>
        mages.OrderByDescending(m => m.Land).ThenBy(m => m.Name, StringComparer.Ordinal);

    public static IEnumerable<Mage> ByNetworth(IEnumerable<Mage> mages, GameRules rules) =>
        mages.OrderByDescending(m => m.Networth(rules)).ThenBy(m => m.Name, StringComparer.Ordinal);

    /// <summary>The median mage's land: the middle one, or the two middle ones' average (rounded down) for an even
    /// count; 0 with no mages. A lone mage is at the median, so it is never below it.</summary>
    public static int MedianLand(IEnumerable<Mage> mages)
    {
        var lands = mages.Select(m => m.Land).Order().ToList();
        return lands.Count == 0 ? 0
            : lands.Count % 2 == 1 ? lands[lands.Count / 2]
            : (lands[(lands.Count / 2) - 1] + lands[lands.Count / 2]) / 2;
    }
}

public sealed record HallOfFameEntry(int Rank, string Name, MagicSchool School, int Land, int Networth);

/// <summary>The top mages of a finished round, kept after the reset.</summary>
public sealed record HallOfFame(
    int RoundNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int MageCount,
    IReadOnlyList<HallOfFameEntry> ByLand,
    IReadOnlyList<HallOfFameEntry> ByNetworth)
{
    public static HallOfFame Record(Round round, IReadOnlyCollection<Mage> mages, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(mages);
        ArgumentNullException.ThrowIfNull(rules);

        return new HallOfFame(
            round.Number,
            round.StartsAt,
            round.EndsAt,
            mages.Count,
            Top(Rankings.ByLand(mages)),
            Top(Rankings.ByNetworth(mages, rules)));

        List<HallOfFameEntry> Top(IEnumerable<Mage> ranked) =>
        [
            .. ranked.Take(rules.HallOfFameSize)
                .Select((m, i) => new HallOfFameEntry(i + 1, m.Name, m.School, m.Land, m.Networth(rules))),
        ];
    }
}
