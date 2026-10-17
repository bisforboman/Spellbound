namespace Spellbound.Web.Data;

/// <summary>
/// Banned players by owner id, in one document. A banned player's account is locked, their actions are refused, and
/// their mage drops out of rankings, targeting and the hall of fame. Unbanning reverses all of it.
/// </summary>
public sealed class BanList
{
    public Dictionary<string, Ban> Bans { get; set; } = [];

    public bool IsBanned(string ownerId) => Bans.ContainsKey(ownerId);
}

/// <param name="By">The admin's email.</param>
public sealed record Ban(DateTimeOffset At, string Reason, string By);
