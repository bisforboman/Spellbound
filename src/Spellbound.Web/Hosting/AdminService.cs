using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using System.Security.Cryptography;
using Raven.Client.Exceptions;
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
    IDocumentStore store,
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
        using var session = store.OpenAsyncSession();
        return (await session.LoadAsync<CurrentRound>(HostStore.CurrentRoundKey))?.Round;
    }

    /// <summary>The current round's players (banned or not), then banned players who don't play this round.</summary>
    public async Task<IReadOnlyList<AdminPlayer>> GetPlayersAsync()
    {
        using var session = store.OpenAsyncSession();
        var players = await RoundPlayersAsync(session);
        var bans = await HostStore.LoadBansAsync(session);

        var ownerIds = players.Keys.Concat(bans.Bans.Keys).Distinct().ToList();
        var users = await session.LoadAsync<ApplicationUser>(ownerIds);

        return
        [
            .. ownerIds
                .Select(id => new AdminPlayer(
                    id, users.GetValueOrDefault(id)?.Email, players.GetValueOrDefault(id), bans.Bans.GetValueOrDefault(id)))
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

        var saved = await ChangeBansAsync(bans => bans.Bans[ownerId] = new Ban(time.GetUtcNow(), reason, adminEmail));
        if (saved is not null)
        {
            return saved;
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
        using (var session = store.OpenAsyncSession())
        {
            if ((await HostStore.LoadBansAsync(session)).IsBanned(ownerId))
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
        var saved = await ChangeBansAsync(bans => bans.Bans.Remove(ownerId));
        if (saved is not null)
        {
            return saved;
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
        catch (ConcurrencyException)
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

        using var session = store.OpenAsyncSession();
        var ownerIds = shared.SelectMany(n => n.OwnerIds).Distinct().ToList();
        var users = await session.LoadAsync<ApplicationUser>(ownerIds);
        var players = await RoundPlayersAsync(session);
        var bans = await HostStore.LoadBansAsync(session);

        return
        [
            .. shared.Select(n => new AdminSharedNetwork(n.Network, n.LastSeen,
            [
                .. n.OwnerIds.Select(id => new AdminPlayer(
                    id,
                    users.GetValueOrDefault(id)?.Email,
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

    private async Task<IReadOnlyDictionary<string, PlayerSummary>> RoundPlayersAsync(IAsyncDocumentSession session) =>
        (await session.LoadAsync<CurrentRound>(HostStore.CurrentRoundKey))?.Round is { } round
            ? await summaries.GetAsync(session, round)
            : new Dictionary<string, PlayerSummary>();

    private async Task<string?> ChangeBansAsync(Action<BanList> change)
    {
        using var session = store.OpenAsyncSession();
        session.Advanced.OptimisticConcurrencyMode = OptimisticConcurrencyMode.Writes;
        var bans = await session.LoadAsync<BanList>(HostStore.BansKey);
        if (bans is null)
        {
            bans = new BanList();
            await session.StoreAsync(bans, HostStore.BansKey);
        }

        change(bans);
        try
        {
            await session.SaveChangesAsync();
            return null;
        }
        catch (ConcurrencyException)
        {
            return "The ban list changed at the same moment. Try again.";
        }
    }

    /// <summary>Runs an account change in its own scope (UserManager's session must not outlive one change).</summary>
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
