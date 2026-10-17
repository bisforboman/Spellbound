using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web;

/// <summary>A player in the current round, or a banned player, as the admin page lists them.</summary>
public sealed record AdminPlayer(string OwnerId, string? Email, Mage? Mage, Ban? Ban);

/// <summary>
/// What the admin page can do: list players, ban and unban, and move the current round's end.
/// Only reachable through pages behind <see cref="AdminPolicy"/>.
/// </summary>
public sealed class AdminService(IDocumentStore store, IServiceScopeFactory scopes, RoundKeeper rounds, TimeProvider time)
{
    public const int MaxReasonLength = 200;

    /// <summary>The current round's mages (banned or not), then banned players who have no mage this round.</summary>
    public async Task<IReadOnlyList<AdminPlayer>> GetPlayersAsync()
    {
        using var session = store.OpenAsyncSession();
        var round = (await session.LoadAsync<CurrentRound>(RavenSetup.CurrentRoundKey))?.Round;
        var mages = round is null ? [] : await RavenSetup.LoadRoundMagesAsync(session, round.Number);
        var bans = await RavenSetup.LoadBansAsync(session);

        var ownerIds = mages.Select(m => m.OwnerId).Concat(bans.Bans.Keys).Distinct().ToList();
        var users = await session.LoadAsync<ApplicationUser>(ownerIds);
        var byOwner = mages.ToDictionary(m => m.OwnerId);

        return
        [
            .. ownerIds
                .Select(id => new AdminPlayer(
                    id, users.GetValueOrDefault(id)?.Email, byOwner.GetValueOrDefault(id), bans.Bans.GetValueOrDefault(id)))
                .OrderBy(p => p.Mage is null)
                .ThenByDescending(p => p.Mage?.Land ?? 0)
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

    private async Task<string?> ChangeBansAsync(Action<BanList> change)
    {
        using var session = store.OpenAsyncSession();
        session.Advanced.OptimisticConcurrencyMode = OptimisticConcurrencyMode.Writes;
        var bans = await session.LoadAsync<BanList>(RavenSetup.BansKey);
        if (bans is null)
        {
            bans = new BanList();
            await session.StoreAsync(bans, RavenSetup.BansKey);
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
