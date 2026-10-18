using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Spellbound.Web.Hosting;

/// <summary>Accounts that signed in from the same network recently: a hint for the admin, never proof.</summary>
public sealed record SharedNetwork(string Network, IReadOnlyList<string> OwnerIds, DateTimeOffset LastSeen);

/// <summary>
/// Multi-account signals. At sign-up and sign-in, records a keyed hash of the player's IP address (never the address
/// itself) with the time. The admin page lists networks that several accounts used recently. Households and mobile
/// carriers share addresses, so the admin judges; nothing is banned automatically.
/// </summary>
public sealed class AccountSignals(HostDatabase database, TimeProvider time)
{
    public const int NetworksKept = 20;

    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private const string KeyName = "signal-key";

    private byte[]? key;

    public async Task RecordAsync(string ownerId, IPAddress? address)
    {
        if (address is null)
        {
            return;
        }

        var network = await HashAsync(address);
        await using var db = await database.BeginAsync();
        await db.ExecuteAsync(
            """
            INSERT INTO account_networks (owner_id, network, last_seen) VALUES ($1, $2, $3)
            ON CONFLICT (owner_id, network) DO UPDATE SET last_seen = excluded.last_seen
            """,
            ownerId, network, time.GetUtcNow());
        await db.ExecuteAsync(
            """
            DELETE FROM account_networks WHERE owner_id = $1 AND network NOT IN
                (SELECT network FROM account_networks WHERE owner_id = $1 ORDER BY last_seen DESC LIMIT $2)
            """,
            ownerId, NetworksKept);
        await db.CommitAsync();
    }

    /// <summary>Networks two or more accounts used within the window, most recent first.</summary>
    public async Task<IReadOnlyList<SharedNetwork>> GetSharedNetworksAsync()
    {
        var since = time.GetUtcNow() - Window;
        await using var db = await database.BeginAsync();
        var uses = await db.QueryAsync(
            """
            SELECT network, owner_id, last_seen FROM account_networks
            WHERE last_seen >= $1 AND network IN
                (SELECT network FROM account_networks WHERE last_seen >= $1 GROUP BY network HAVING count(*) > 1)
            """,
            r => (Network: r.GetString(0), OwnerId: r.GetString(1), LastSeen: r.GetFieldValue<DateTimeOffset>(2)),
            since);

        return
        [
            .. uses.GroupBy(u => u.Network)
                .Select(g => new SharedNetwork(g.Key, [.. g.Select(u => u.OwnerId).Order()], g.Max(u => u.LastSeen)))
                .OrderByDescending(n => n.LastSeen),
        ];
    }

    /// <summary>A short, stable label for an address. Keyed with a per-server secret, so it can't be reversed by guessing.</summary>
    private async Task<string> HashAsync(IPAddress address)
    {
        key ??= await LoadOrCreateKeyAsync();
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(address.MapToIPv6().ToString()));
        return Convert.ToHexString(hash, 0, 6);
    }

    private async Task<byte[]> LoadOrCreateKeyAsync()
    {
        await using var db = await database.BeginAsync();
        await db.ExecuteAsync(
            "INSERT INTO server_secrets (name, value) VALUES ($1, $2) ON CONFLICT (name) DO NOTHING",
            KeyName, RandomNumberGenerator.GetBytes(32)); // the first request creates it; later ones keep it
        var key = (await db.ScalarAsync<byte[]>("SELECT value FROM server_secrets WHERE name = $1", KeyName))!;
        await db.CommitAsync();
        return key;
    }
}
