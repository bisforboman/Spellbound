using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>A player in the current round, or a banned player, as the admin page lists them.</summary>
/// <param name="Player">How the game describes them this round; null if they don't play this round.</param>
public sealed record AdminPlayer(string OwnerId, string? Email, PlayerSummary? Player, Ban? Ban);

/// <summary>Accounts that recently signed in from the same network (see <see cref="AccountSignals"/>).</summary>
public sealed record AdminSharedNetwork(string Network, DateTimeOffset LastSeen, IReadOnlyList<AdminPlayer> Players);

/// <summary>
/// What the admin page can do: list players, ban and unban, and move the current round's end.
/// Only reachable through pages behind <see cref="AdminPolicy"/>.
/// </summary>
public sealed class AdminService(
    HostDatabase database,
    RoundKeeper rounds,
    BackupKeeper backups,
    AccountSignals signals,
    IPlayerSummaries summaries,
    IRoundLifecycle game,
    TimeProvider time)
{
    public const int MaxReasonLength = 200;

    public async Task<Round?> GetRoundAsync()
    {
        await using var db = await database.BeginAsync();
        return (await db.LoadRunningGameAsync(game.AlwaysOnMode))?.Round;
    }

    /// <summary>The current round's players (banned or not), then banned players who don't play this round.</summary>
    public async Task<IReadOnlyList<AdminPlayer>> GetPlayersAsync()
    {
        await using var db = await database.BeginAsync();
        var players = await RoundPlayersAsync(db);
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

    /// <summary>Moves the current round's end. An end at or before now ends the round at once and starts the next.</summary>
    public async Task<string> ChangeRoundEndAsync(DateTimeOffset endsAt)
    {
        var before = (await rounds.TickAsync()).Round;
        if (endsAt <= before.StartsAt)
        {
            return $"Round {before.Number} started {before.StartsAt:u}; its end must be after that.";
        }

        var after = (await rounds.ChangeEndAsync(endsAt)).Round;
        return after.Number == before.Number
            ? $"Round {after.Number} now ends {after.EndsAt:u}."
            : $"Round {before.Number} has ended and round {after.Number} has started.";
    }

    /// <summary>Networks several accounts used in the last 30 days, with who they are this round.</summary>
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
        var players = await RoundPlayersAsync(db);
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

    private async Task<IReadOnlyDictionary<string, PlayerSummary>> RoundPlayersAsync(DbSession db) =>
        await db.LoadRunningGameAsync(game.AlwaysOnMode) is { } running
            ? await summaries.GetAsync(db, running)
            : new Dictionary<string, PlayerSummary>();

    private async Task<string> EmailOrIdAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        return await db.ScalarAsync<string>("SELECT email FROM accounts WHERE id = $1", ownerId) ?? ownerId;
    }
}
