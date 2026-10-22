using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>The game's own tables in the host's database: its SQL scripts, migrated after the host's.</summary>
public interface IGameSchema
{
    SchemaModule Schema { get; }
}

/// <summary>
/// A game the server runs: its id, its mode (the game's own name for a kind of game), its clock (number within its mode,
/// start, and the end by which it is wrapped up at the latest), its rules, which the game writes and reads and the host
/// only stores, and when it was wrapped up (null while it runs).
/// </summary>
public sealed record HostedGame(long Id, string Mode, Round Round, string RulesJson, DateTimeOffset? EndedAt = null)
{
    public bool IsRunning => EndedAt is null;
}

/// <summary>What the host needs to start a game: its mode, how long it may run and the rules it runs under.</summary>
public sealed record NewGame(string Mode, TimeSpan Length, string RulesJson);

/// <summary>
/// What the hosting code needs from the game about its games. The game starts its own games (on demand, through
/// <see cref="HostStore.InsertGameAsync"/>); the host ends any still running at their end (<see cref="GameKeeper"/>) or
/// when an admin ends one.
/// </summary>
public interface IGameLifecycle
{
    /// <summary>
    /// Called once when a game is ended by the host, inside the transaction that ends it: anything written through
    /// <paramref name="db"/> here is saved with it (or not at all). Banned players are in <paramref name="bans"/>; leave
    /// them out of anything public, such as a hall of fame.
    /// </summary>
    Task OnGameEndedAsync(DbSession db, HostedGame ended, BanList bans, CancellationToken cancellationToken);

    /// <summary>A short line about where a game is, for the admin page (for example its round).</summary>
    Task<string> DescribeAsync(DbSession db, HostedGame game);

    /// <summary>
    /// Called by <see cref="GameKeeper"/> on each check, after it has ended overdue games and outside any transaction:
    /// the game's chance to act on its own deadlines (for example a step that resolves when its time is up). Open a
    /// transaction per game through <paramref name="database"/>, so one game's problem doesn't hold up the rest.
    /// </summary>
    Task OnTickAsync(HostDatabase database, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
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
