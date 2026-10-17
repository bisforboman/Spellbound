namespace Spellbound.Web.Hosting;

/// <summary>
/// Banned players by owner id. A banned player's account is locked, their actions are refused, and their mage drops
/// out of rankings, targeting and the hall of fame. Unbanning reverses all of it.
/// </summary>
public sealed class BanList(IReadOnlyDictionary<string, Ban> bans)
{
    public BanList()
        : this(new Dictionary<string, Ban>())
    {
    }

    public IReadOnlyDictionary<string, Ban> Bans => bans;

    public bool IsBanned(string ownerId) => bans.ContainsKey(ownerId);
}

/// <summary>A banned player (one row of the bans table).</summary>
public sealed class Ban
{
    public string OwnerId { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>The admin's email.</summary>
    public string By { get; set; } = string.Empty;
}
