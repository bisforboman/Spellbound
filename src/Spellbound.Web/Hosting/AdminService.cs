using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>A player in a running game, or a banned player, as the admin page lists them.</summary>
/// <param name="Player">How the game describes them (in their first running game); null if they play in none.</param>
public sealed record AdminPlayer(string OwnerId, string? Email, PlayerSummary? Player, Ban? Ban);

/// <summary>Accounts that recently signed in from the same network (see <see cref="AccountSignals"/>).</summary>
public sealed record AdminSharedNetwork(string Network, DateTimeOffset LastSeen, IReadOnlyList<AdminPlayer> Players);

/// <summary>A running game as the admin page lists it: what the game says about it, and who plays.</summary>
public sealed record AdminGame(HostedGame Game, string Description, IReadOnlyList<string> Players);

/// <summary>
/// What the admin page can do: list running games and end one, list players, ban and unban.
/// Only reachable through pages behind <see cref="AdminPolicy"/>.
/// </summary>
public sealed class AdminService(
    HostDatabase database,
    GameKeeper games,
    BackupKeeper backups,
    AccountSignals signals,
    IPlayerSummaries summaries,
    IGameLifecycle game,
    TimeProvider time)
{
    public const int MaxReasonLength = 200;

    /// <summary>Every running game, oldest first, with the game's description and its players' names.</summary>
    public async Task<IReadOnlyList<AdminGame>> GetRunningGamesAsync()
    {
        await using var db = await database.BeginAsync();
        var running = new List<AdminGame>();
        foreach (var hosted in await db.LoadRunningGamesAsync())
        {
            var players = await summaries.GetAsync(db, hosted);
            running.Add(new AdminGame(hosted, await game.DescribeAsync(db, hosted), [.. players.Values.Select(p => p.Name).Order()]));
        }

        return running;
    }

    /// <summary>Ends a running game now, with the game's wrap-up.</summary>
    public async Task<string> EndGameNowAsync(long id) =>
        await games.EndAsync(id) ? $"Game {id} has ended." : $"Game {id} isn't running.";

    /// <summary>Players in running games (banned or not), then banned players who play in none.</summary>
    public async Task<IReadOnlyList<AdminPlayer>> GetPlayersAsync()
    {
        await using var db = await database.BeginAsync();
        var players = await PlayersInGamesAsync(db);
        var bans = await db.LoadBansAsync();

        var ownerIds = players.Keys.Concat(bans.Bans.Keys).Distinct().ToList();
        var users = await AccountService.EmailsAsync(db, ownerIds);

        return
        [
            .. ownerIds
                .Select(id => new AdminPlayer(
                    id, users.GetValueOrDefault(id), players.GetValueOrDefault(id), bans.Bans.GetValueOrDefault(id)))
                .OrderBy(p => p.Player is null)
                .ThenByDescending(p => p.Player?.Rank ?? 0)
                .ThenBy(p => p.Email, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Bans a player: effective at once in the game, and their sign-in stops working (see AccountEndpoints).</summary>
    public async Task<string> BanAsync(string adminOwnerId, string adminEmail, string ownerId, string reason)
    {
        reason = reason.Trim();
        if (ownerId == adminOwnerId)
        {
            return "You can't ban yourself.";
        }

        if (reason.Length is 0 or > MaxReasonLength)
        {
            return $"Give a reason of 1–{MaxReasonLength} characters.";
        }

        await using (var db = await database.BeginAsync())
        {
            await db.ExecuteAsync(
                """
                INSERT INTO bans (owner_id, at, reason, banned_by) VALUES ($1, $2, $3, $4)
                ON CONFLICT (owner_id) DO UPDATE SET at = excluded.at, reason = excluded.reason, banned_by = excluded.banned_by
                """,
                ownerId, time.GetUtcNow(), reason, adminEmail);
            await db.CommitAsync();
        }

        // The account's sign-in cookie is checked within a minute (open game connections within a few) and signed out.
        return $"Banned {await EmailOrIdAsync(ownerId)}.";
    }

    public async Task<string> UnbanAsync(string ownerId)
    {
        await using (var db = await database.BeginAsync())
        {
            await db.ExecuteAsync("DELETE FROM bans WHERE owner_id = $1", ownerId);
            await db.CommitAsync();
        }

        return $"Unbanned {await EmailOrIdAsync(ownerId)}.";
    }

    /// <summary>Networks several accounts used in the last 30 days, with who they are in running games.</summary>
    public async Task<IReadOnlyList<AdminSharedNetwork>> GetSharedNetworksAsync()
    {
        var shared = await signals.GetSharedNetworksAsync();
        if (shared.Count == 0)
        {
            return [];
        }

        await using var db = await database.BeginAsync();
        var ownerIds = shared.SelectMany(n => n.OwnerIds).Distinct().ToList();
        var users = await AccountService.EmailsAsync(db, ownerIds);
        var players = await PlayersInGamesAsync(db);
        var bans = await db.LoadBansAsync();

        return
        [
            .. shared.Select(n => new AdminSharedNetwork(n.Network, n.LastSeen,
            [
                .. n.OwnerIds.Select(id => new AdminPlayer(
                    id,
                    users.GetValueOrDefault(id),
                    players.GetValueOrDefault(id),
                    bans.Bans.GetValueOrDefault(id))),
            ])),
        ];
    }

    public bool BackupsEnabled => backups.Directory is not null;

    public DateTimeOffset? LastBackupAt() => backups.LastBackupAt();

    public async Task<string> BackUpNowAsync()
    {
        if (!BackupsEnabled)
        {
            return "Backups are off: set Backup:Directory.";
        }

        var file = await backups.BackUpNowAsync();
        return $"Backed up to {Path.GetFileName(file)}.";
    }

    /// <summary>Everyone in a running game, described by their first (oldest) one.</summary>
    private async Task<IReadOnlyDictionary<string, PlayerSummary>> PlayersInGamesAsync(DbSession db)
    {
        var players = new Dictionary<string, PlayerSummary>();
        foreach (var running in await db.LoadRunningGamesAsync())
        {
            foreach (var (ownerId, summary) in await summaries.GetAsync(db, running))
            {
                players.TryAdd(ownerId, summary);
            }
        }

        return players;
    }

    private async Task<string> EmailOrIdAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        return await db.ScalarAsync<string>("SELECT email FROM accounts WHERE id = $1", ownerId) ?? ownerId;
    }
}
