namespace Spellbound.Web.Data;

/// <summary>A mage's recent action reports, newest first, stored as one document next to the mage.</summary>
public sealed class NewsLog
{
    public List<NewsEntry> Entries { get; set; } = [];

    /// <summary>Hostile entries (attacks and hexes) the player hasn't seen on the overview yet.</summary>
    public int Unseen { get; set; }

    public void Add(NewsEntry entry, int keep)
    {
        if (entry.Hostile)
        {
            Unseen++;
        }

        Entries.Insert(0, entry);
        if (Entries.Count > keep)
        {
            Entries.RemoveRange(keep, Entries.Count - keep);
        }
    }
}

/// <param name="Hostile">Written by another mage's attack or hex.</param>
public sealed record NewsEntry(DateTimeOffset At, IReadOnlyList<string> Lines, bool Hostile = false);
