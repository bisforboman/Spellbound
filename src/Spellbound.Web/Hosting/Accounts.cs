using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Spellbound.Web.Hosting;

/// <summary>
/// A player's account. Players sign in with an outside provider (Google; "dev" in Development), so no passwords are
/// stored. <see cref="Id"/> is our own, and it is the owner id everywhere else (never the provider's id), so another
/// sign-in method can later link to the same account. It is derived from the provider and the provider's id
/// (<see cref="AccountService.IdFor"/>), so signing in again after deleting the account gets the same id, and a ban
/// on it still holds.
/// </summary>
public sealed class Account
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The sign-in provider, e.g. "google".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's id for the person (Google's "sub"), which never changes.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The email the provider gave at the last sign-in. Admins are recognised by it.</summary>
    public string Email { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSignInAt { get; set; }
}

/// <summary>The result of a sign-in: the account, or why it was refused.</summary>
public sealed record SignInResult(Account? Account, string? Refused);

/// <summary>Accounts: sign-in, the checks that keep a banned or deleted account signed out, and deletion.</summary>
public sealed class AccountService(IDbContextFactory<HostDb> dbs, TimeProvider time, IAccountLifecycle game)
{
    public const string Banned = "Your account is banned.";

    /// <summary>Finds the provider's account, or creates it on first sign-in. Refuses banned accounts.</summary>
    public async Task<SignInResult> SignInAsync(string provider, string subject, string email)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return new(null, "The sign-in didn't say who you are. Please try again.");
        }

        await using var db = await dbs.CreateDbContextAsync();
        var now = time.GetUtcNow();
        var id = IdFor(provider, subject);
        if (await db.Bans.AnyAsync(b => b.OwnerId == id))
        {
            return new(null, Banned); // checked first, so it holds even after the account was deleted
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id);
        if (account is null)
        {
            account = db.Accounts.Add(new Account
            {
                Id = id,
                Provider = provider,
                Subject = subject,
                CreatedAt = now,
            }).Entity;
        }

        (account.Email, account.LastSignInAt) = (email.Trim(), now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (HostStore.IsDuplicateKey(ex))
        {
            return await SignInAsync(provider, subject, email); // the same person signed in twice at once
        }

        return new(account, null);
    }

    /// <summary>A stable account id for a provider's person: a hash, so the provider's own id isn't spread around.</summary>
    public static string IdFor(string provider, string subject) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}\n{subject}")))[..32];

    /// <summary>The claims the sign-in cookie carries: our account id, and the email (as the name) for the admin check.</summary>
    public static ClaimsPrincipal Principal(Account account, string authenticationType) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id),
                new Claim(ClaimTypes.Name, account.Email),
                new Claim(ClaimTypes.Email, account.Email),
            ],
            authenticationType));

    /// <summary>Whether a signed-in account may stay signed in: it still exists and isn't banned.</summary>
    public async Task<bool> IsActiveAsync(ClaimsPrincipal user)
    {
        if (user.FindFirstValue(ClaimTypes.NameIdentifier) is not { } id)
        {
            return false;
        }

        await using var db = await dbs.CreateDbContextAsync();
        return await db.Accounts.AnyAsync(a => a.Id == id) && !await db.Bans.AnyAsync(b => b.OwnerId == id);
    }

    public async Task<Account?> FindAsync(string id)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Account?> FindByEmailAsync(string email)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Email.ToUpper() == normalized);
    }

    /// <summary>
    /// Deletes an account with its network hashes and, through <see cref="IAccountLifecycle"/>, the player's game data,
    /// all in one transaction. Bans stay: the id comes back on the next sign-in, so a ban can't be shed this way.
    /// </summary>
    public async Task DeleteAsync(string id)
    {
        await using var db = await dbs.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.AccountNetworks.Where(n => n.OwnerId == id).ExecuteDeleteAsync();
        await game.OnAccountDeletedAsync(db, id);
        await db.Accounts.Where(a => a.Id == id).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }
}
