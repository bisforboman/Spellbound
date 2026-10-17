namespace Spellbound.Web.Data;

/// <summary>One action report in a mage's news log. Order by <see cref="Id"/>: SQLite can't sort DateTimeOffset.</summary>
public sealed class NewsEntry
{
    public long Id { get; set; }

    public Guid MageId { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>The report's lines, joined with '\n'.</summary>
    public string Text { get; set; } = string.Empty;
}
