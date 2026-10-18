using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>The game's own tables in the host's database: its SQL scripts, migrated after the host's.</summary>
public interface IGameSchema
{
    SchemaModule Schema { get; }
}

/// <summary>
/// A game the server runs: its id, its mode (the game's own name for a kind of game), its round (number, start and end)
/// and its rules, which the game writes and reads and the host only stores.
/// </summary>
public sealed record HostedGame(long Id, string Mode, Round Round, string RulesJson);

/// <summary>What the host needs to start a game: its mode, how long it lasts and the rules it runs under.</summary>
public sealed record NewGame(string Mode, TimeSpan Length, string RulesJson);

/// <summary>What the hosting code needs from the game about games and their rounds.</summary>
public interface IRoundLifecycle
{
    /// <summary>
    /// The game the server always keeps running: when one ends, the next of the same mode starts. Asked each time one
    /// starts, so it can carry the settings in force then.
    /// </summary>
    NewGame NextAlwaysOn();

    /// <summary>The mode of the game the server always keeps running (<see cref="NextAlwaysOn"/>'s).</summary>
    string AlwaysOnMode { get; }

    /// <summary>
    /// Called once when a game has ended, before the next one starts, inside the transaction that moves it on:
    /// anything written through <paramref name="db"/> here is saved with it (or not at all). Banned players are in
    /// <paramref name="bans"/>; leave them out of anything public, such as a hall of fame.
    /// </summary>
    Task OnGameEndedAsync(DbSession db, HostedGame ended, BanList bans, CancellationToken cancellationToken);
}

/// <summary>What the hosting code tells the game about accounts.</summary>
public interface IAccountLifecycle
{
    /// <summary>
    /// Called when an account is deleted, inside the transaction that deletes it: delete the player's game data here,
    /// and it goes with the account (or not at all).
    /// </summary>
    Task OnAccountDeletedAsync(DbSession db, string ownerId);

    /// <summary>
    /// Everything the game stores about the player, for the "download my data" file on the Account page. Return
    /// something System.Text.Json can write.
    /// </summary>
    Task<object> ExportAsync(DbSession db, string ownerId);
}

/// <summary>How a player looks on the admin page.</summary>
/// <param name="Name">The player's in-game name.</param>
/// <param name="Details">A short line of game facts, for example school and land.</param>
/// <param name="Rank">Higher sorts first.</param>
public sealed record PlayerSummary(string Name, string Details, long Rank);

/// <summary>What the hosting code needs from the game about players.</summary>
public interface IPlayerSummaries
{
    /// <summary>Every player in the game, by owner (account) id.</summary>
    Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(DbSession db, HostedGame game);
}

/// <summary>Game-specific sections the admin page shows after its own (components with no parameters).</summary>
public sealed class AdminPageOptions
{
    public List<Type> GameSections { get; } = [];
}
