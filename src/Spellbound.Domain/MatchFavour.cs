namespace Spellbound.Domain;

/// <summary>Puerto Rico's unpicked roles (docs/game-design.md, Rounds and stances): Attune, Summon or Build that nobody
/// chose in a round costs less in the next, more for each round it waits, up to a cap; choosing it resets it.</summary>
public sealed partial class Match
{
    /// <summary>The stances that get cheaper when nobody chooses them.</summary>
    public static readonly IReadOnlyList<Stance> FavouredStances = [Stance.Attune, Stance.Summon, Stance.Build];

    private readonly Dictionary<Stance, int> favour = [];

    /// <summary>How much less a stance costs this round, because nobody chose it lately.</summary>
    public int FavourOf(Stance stance) => favour.GetValueOrDefault(stance);

    /// <summary>A stance's full price with this round's favour taken off, never below zero.</summary>
    private int Favoured(Stance stance, int full) => Math.Max(0, full - FavourOf(stance));

    /// <summary>At the end of a round: a stance nobody chose gains favour for the next; one somebody chose loses it.</summary>
    private void UpdateFavour(IEnumerable<Orders> orders, ReportLines lines)
    {
        var chosen = orders.Where(o => o.Target is not null).Select(o => o.Stance).ToHashSet();
        foreach (var stance in FavouredStances)
        {
            var before = FavourOf(stance);
            favour[stance] = chosen.Contains(stance) ? 0 : Math.Min(Rules.FavourCap, before + Rules.FavourPerRound);
            if (favour[stance] > 0 && favour[stance] != before)
            {
                lines.Add($"Nobody chose {stance} this round: next round it costs {favour[stance]} mana less.");
            }
        }
    }
}
