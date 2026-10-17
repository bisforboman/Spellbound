using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    IDbContextFactory<HostDb> dbs,
    IServiceScopeFactory scopes,
    RoundKeeper rounds,
    BackupKeeper backups,
    AccountSignals signals,
    IPlayerSummaries summaries,
    TimeProvider time)
{
    public const int MaxReasonLength = 200;

    public async Task<Round?> GetRoundAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        return (await db.LoadCurrentRoundAsync())?.Round;
    }

    /// <summary>The current round's players (banned or not), then banned players who don't play this round.</summary>
    public async Task<IReadOnlyList<AdminPlayer>> GetPlayersAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        var players = await RoundPlayersAsync(db);
        var bans = await db.LoadBansAsync();

        var ownerIds = players.Keys.Concat(bans.Bans.Keys).Distinct().ToList();
        var users = await EmailsAsync(db, ownerIds);

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

    /// <summary>Bans a player: adds them to the ban list (effective at once in the game) and locks their account.</summary>
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

        await using (var db = await dbs.CreateDbContextAsync())
        {
            var ban = await db.Bans.FindAsync(ownerId);
            if (ban is null)
            {
                ban = db.Bans.Add(new Ban { OwnerId = ownerId }).Entity;
            }

            (ban.At, ban.Reason, ban.By) = (time.GetUtcNow(), reason, adminEmail);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (HostStore.IsDuplicateKey(ex))
            {
                return "That player was banned at the same moment. Check the list.";
            }
        }

        // Locking the account stops new sign-ins; the new security stamp signs out existing sessions when they revalidate.
        return await WithUserAsync(ownerId, async (users, user) =>
        {
            await users.SetLockoutEnabledAsync(user, true);
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            await users.UpdateSecurityStampAsync(user);
            return $"Banned {user.Email}.";
        });
    }

    /// <summary>
    /// Gives a player a new, temporary password, for when they've forgotten theirs (the server sends no email). The
    /// admin passes it on; the player logs in with it and picks their own under Account. Signs them out elsewhere.
    /// </summary>
    public async Task<string> ResetPasswordAsync(string ownerId)
    {
        await using (var db = await dbs.CreateDbContextAsync())
        {
            if (await db.Bans.AnyAsync(b => b.OwnerId == ownerId))
            {
                return "That player is banned. Unban them first if they should get back in.";
            }
        }

        return await WithUserAsync(ownerId, async (users, user) =>
        {
            var password = TemporaryPassword();
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, password);
            if (!result.Succeeded)
            {
                return $"Couldn't reset the password: {string.Join(" ", result.Errors.Select(e => e.Description))}";
            }

            await users.ResetAccessFailedCountAsync(user);
            await users.SetLockoutEndDateAsync(user, null); // lift a lockout from their wrong guesses (bans are refused above)
            return $"Temporary password for {user.Email}: {password} (shown once; give it to them privately).";
        });
    }

    /// <summary>The same, for any account by email, including players with no mage this round.</summary>
    public async Task<string> ResetPasswordByEmailAsync(string email)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.FindByEmailAsync(email.Trim()) is { Id: { } ownerId }
            ? await ResetPasswordAsync(ownerId)
            : "No account has that email.";
    }

    /// <summary>
    /// Three groups of four letters and digits separated by dashes, e.g. "Kq7m-Pz2r-Hb9w": easy to read out, and it
    /// meets Identity's rules (upper, lower, digit and a symbol). Leaves out look-alikes such as 0/O and 1/l/I.
    /// </summary>
    public static string TemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        var groups = new string[3];
        for (var g = 0; g < groups.Length; g++)
        {
            groups[g] = string.Concat(
                Pick(upper), Pick(lower), Pick(digits), Pick(lower));
        }

        return string.Join('-', groups);

        static char Pick(string from) => from[RandomNumberGenerator.GetInt32(from.Length)];
    }

    public async Task<string> UnbanAsync(string ownerId)
    {
        await using (var db = await dbs.CreateDbContextAsync())
        {
            await db.Bans.Where(b => b.OwnerId == ownerId).ExecuteDeleteAsync();
        }

        return await WithUserAsync(ownerId, async (users, user) =>
        {
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
            return $"Unbanned {user.Email}.";
        });
    }

    /// <summary>Moves the current round's end. An end at or before now ends the round at once and starts the next.</summary>
    public async Task<string> ChangeRoundEndAsync(DateTimeOffset endsAt)
    {
        var before = await rounds.TickAsync();
        if (endsAt <= before.StartsAt)
        {
            return $"Round {before.Number} started {before.StartsAt:u}; its end must be after that.";
        }

        try
        {
            var after = await rounds.ChangeEndAsync(endsAt);
            return after.Number == before.Number
                ? $"Round {after.Number} now ends {after.EndsAt:u}."
                : $"Round {before.Number} has ended and round {after.Number} has started.";
        }
        catch (DbUpdateConcurrencyException)
        {
            return "The round changed at the same moment. Check it and try again.";
        }
    }

    /// <summary>Networks several accounts used in the last 30 days, with who they are this round.</summary>
    public async Task<IReadOnlyList<AdminSharedNetwork>> GetSharedNetworksAsync()
    {
        var shared = await signals.GetSharedNetworksAsync();
        if (shared.Count == 0)
        {
            return [];
        }

        await using var db = await dbs.CreateDbContextAsync();
        var ownerIds = shared.SelectMany(n => n.OwnerIds).Distinct().ToList();
        var users = await EmailsAsync(db, ownerIds);
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

    private async Task<IReadOnlyDictionary<string, PlayerSummary>> RoundPlayersAsync(HostDb db) =>
        (await db.LoadCurrentRoundAsync())?.Round is { } round
            ? await summaries.GetAsync(db, round)
            : new Dictionary<string, PlayerSummary>();

    private static async Task<Dictionary<string, string?>> EmailsAsync(HostDb db, IReadOnlyCollection<string> ownerIds) =>
        await db.Users.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);

    /// <summary>Runs an account change in its own scope (UserManager's context must not outlive one change).</summary>
    private async Task<string> WithUserAsync(
        string ownerId, Func<UserManager<ApplicationUser>, ApplicationUser, Task<string>> change)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.FindByIdAsync(ownerId) is { } user
            ? await change(users, user)
            : "The ban list is updated, but that account no longer exists.";
    }
}
