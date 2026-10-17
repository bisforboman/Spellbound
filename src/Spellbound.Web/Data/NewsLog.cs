namespace Spellbound.Web.Data;

/// <summary>A mage's recent action reports, newest first, stored as one document next to the mage.</summary>
public sealed class NewsLog
{
    public List<NewsEntry> Entries { get; set; } = [];

    public void Add(NewsEntry entry, int keep)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > keep)
        {
            Entries.RemoveRange(keep, Entries.Count - keep);
        }
    }
}

public sealed record NewsEntry(DateTimeOffset At, IReadOnlyList<string> Lines);
