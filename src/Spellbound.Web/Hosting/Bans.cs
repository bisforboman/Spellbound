namespace Spellbound.Web.Hosting;

/// <summary>
/// Banned players by owner id. A banned player can't sign in, and the game leaves them out of anything public (see
/// <see cref="IGameLifecycle.OnGameEndedAsync"/>). Unbanning reverses it.
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
/// <param name="By">The admin's email.</param>
public sealed record Ban(string OwnerId, DateTimeOffset At, string Reason, string By);
