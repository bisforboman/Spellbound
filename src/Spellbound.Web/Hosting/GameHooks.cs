using Raven.Client.Documents.Session;
using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>What the hosting code needs from the game about rounds.</summary>
public interface IRoundLifecycle
{
    /// <summary>How long each round lasts.</summary>
    TimeSpan RoundLength { get; }

    /// <summary>
    /// Called once when a round has ended, before the next one starts, inside the session that moves the round
    /// pointer: anything stored here is saved in the same transaction (or not at all). Banned players are in
    /// <paramref name="bans"/>; leave them out of anything public, such as a hall of fame.
    /// </summary>
    Task OnRoundEndedAsync(IAsyncDocumentSession session, Round ended, BanList bans, CancellationToken cancellationToken);
}

/// <summary>What the hosting code tells the game about accounts.</summary>
public interface IAccountLifecycle
{
    /// <summary>
    /// Called when an account is deleted, inside the session that deletes it: delete the player's game data here, and
    /// it goes in the same transaction (or not at all).
    /// </summary>
    Task OnAccountDeletedAsync(IAsyncDocumentSession session, string ownerId);
}

/// <summary>How a player looks on the admin page.</summary>
/// <param name="Name">The player's in-game name.</param>
/// <param name="Details">A short line of game facts, for example school and land.</param>
/// <param name="Rank">Higher sorts first.</param>
public sealed record PlayerSummary(string Name, string Details, long Rank);

/// <summary>What the hosting code needs from the game about players.</summary>
public interface IPlayerSummaries
{
    /// <summary>Every player in the round, by owner (account) id.</summary>
    Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(IAsyncDocumentSession session, Round round);
}

/// <summary>Game-specific sections the admin page shows after its own (components with no parameters).</summary>
public sealed class AdminPageOptions
{
    public List<Type> GameSections { get; } = [];
}
