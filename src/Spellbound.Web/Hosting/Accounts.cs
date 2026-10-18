using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Spellbound.Web.Hosting;

/// <summary>
/// A player's account. Players sign in with an outside provider (Google; "dev" in Development), so no passwords are
/// stored. <see cref="Id"/> is our own, and it is the owner id everywhere else (never the provider's id), so another
/// sign-in method can later link to the same account. It is derived from the provider and the provider's id
/// (<see cref="AccountService.IdFor"/>), so signing in again after deleting the account gets the same id, and a ban
/// on it still holds.
/// </summary>
/// <param name="Provider">The sign-in provider, e.g. "google".</param>
/// <param name="Email">The email the provider gave at the last sign-in. Admins are recognised by it.</param>
public sealed record Account(string Id, string Provider, string Email, DateTimeOffset CreatedAt, DateTimeOffset LastSignInAt);

/// <summary>The result of a sign-in: the account, or why it was refused.</summary>
public sealed record SignInResult(Account? Account, string? Refused);

/// <summary>Accounts: sign-in, the checks that keep a banned or deleted account signed out, and deletion.</summary>
public sealed class AccountService(HostDatabase database, TimeProvider time, IAccountLifecycle game)
{
    public const string Banned = "Your account is banned.";

    private const string Columns = "id, provider, email, created_at, last_sign_in_at";

    /// <summary>Finds the provider's account, or creates it on first sign-in. Refuses banned accounts.</summary>
    public async Task<SignInResult> SignInAsync(string provider, string subject, string email)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return new(null, "The sign-in didn't say who you are. Please try again.");
        }

        var id = IdFor(provider, subject);
        await using var db = await database.BeginAsync();
        if (await db.IsBannedAsync(id))
        {
            return new(null, Banned); // checked first, so it holds even after the account was deleted
        }

        var now = time.GetUtcNow();
        var account = await db.SingleAsync(
            $"""
            INSERT INTO accounts (id, provider, subject, email, created_at, last_sign_in_at) VALUES ($1, $2, $3, $4, $5, $5)
            ON CONFLICT (id) DO UPDATE SET email = excluded.email, last_sign_in_at = excluded.last_sign_in_at
            RETURNING {Columns}
            """,
            Read, id, provider, subject, email.Trim(), now);
        await db.CommitAsync();
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

        await using var db = await database.BeginAsync();
        return await db.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM accounts WHERE id = $1) AND NOT EXISTS (SELECT 1 FROM bans WHERE owner_id = $1)", id);
    }

    /// <summary>
    /// Everything stored about an account, for the player's "download my data" file: the account row as stored
    /// (including the provider's id for them), their recent networks (hashes, never addresses) and the game's data.
    /// </summary>
    public async Task<object> ExportAsync(string id)
    {
        await using var db = await database.BeginAsync();
        var account = await db.SingleAsync(
            "SELECT provider, subject, email, created_at, last_sign_in_at FROM accounts WHERE id = $1",
            r => new
            {
                provider = r.GetString(0),
                providerId = r.GetString(1),
                email = r.GetString(2),
                createdAt = r.GetFieldValue<DateTimeOffset>(3),
                lastSignInAt = r.GetFieldValue<DateTimeOffset>(4),
            },
            id);
        var networks = await db.QueryAsync(
            "SELECT network, last_seen FROM account_networks WHERE owner_id = $1 ORDER BY last_seen DESC",
            r => new { network = r.GetString(0), lastSeen = r.GetFieldValue<DateTimeOffset>(1) },
            id);
        var push = await db.QueryAsync(
            "SELECT endpoint, created_at FROM push_subscriptions WHERE owner_id = $1 ORDER BY created_at",
            r => new { endpoint = r.GetString(0), createdAt = r.GetFieldValue<DateTimeOffset>(1) },
            id);
        return new { accountId = id, account, signInNetworks = networks, notifications = push, game = await game.ExportAsync(db, id) };
    }

    public async Task<Account?> FindAsync(string id)
    {
        await using var db = await database.BeginAsync();
        return await db.SingleAsync($"SELECT {Columns} FROM accounts WHERE id = $1", Read, id);
    }

    /// <summary>The account with this email (any letter case), or null.</summary>
    public async Task<Account?> FindByEmailAsync(string email)
    {
        await using var db = await database.BeginAsync();
        return (await db.QueryAsync($"SELECT {Columns} FROM accounts WHERE lower(email) = lower($1) ORDER BY id", Read, email.Trim()))
            .FirstOrDefault();
    }

    /// <summary>The emails of these accounts, by id (missing ids are left out).</summary>
    public static async Task<Dictionary<string, string>> EmailsAsync(DbSession db, IReadOnlyCollection<string> ids) =>
        (await db.QueryAsync("SELECT id, email FROM accounts WHERE id = ANY($1)", r => (Id: r.GetString(0), Email: r.GetString(1)), (object)ids.ToArray())) // one array parameter
            .ToDictionary(a => a.Id, a => a.Email);

    /// <summary>
    /// Deletes an account with its network hashes and, through <see cref="IAccountLifecycle"/>, the player's game data,
    /// all in one transaction. Bans stay: the id comes back on the next sign-in, so a ban can't be shed this way.
    /// </summary>
    public async Task DeleteAsync(string id)
    {
        await using var db = await database.BeginAsync();
        await db.ExecuteAsync("DELETE FROM account_networks WHERE owner_id = $1", id);
        await db.ExecuteAsync("DELETE FROM push_subscriptions WHERE owner_id = $1", id);
        await game.OnAccountDeletedAsync(db, id);
        await db.ExecuteAsync("DELETE FROM accounts WHERE id = $1", id);
        await db.CommitAsync();
    }

    private static Account Read(Npgsql.NpgsqlDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetFieldValue<DateTimeOffset>(3), r.GetFieldValue<DateTimeOffset>(4));
}
