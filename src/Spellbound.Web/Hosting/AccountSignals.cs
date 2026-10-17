using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Spellbound.Web.Hosting;

/// <summary>Accounts that signed in from the same network recently: a hint for the admin, never proof.</summary>
public sealed record SharedNetwork(string Network, IReadOnlyList<string> OwnerIds, DateTimeOffset LastSeen);

/// <summary>
/// Multi-account signals. At sign-up and sign-in, records a keyed hash of the player's IP address (never the address
/// itself) with the time. The admin page lists networks that several accounts used recently. Households and mobile
/// carriers share addresses, so the admin judges; nothing is banned automatically.
/// </summary>
public sealed class AccountSignals(IDbContextFactory<HostDb> dbs, TimeProvider time)
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
        await using var db = await dbs.CreateDbContextAsync();
        var networks = await db.AccountNetworks.Where(n => n.OwnerId == ownerId).ToListAsync();
        if (networks.Find(n => n.Network == network) is { } seen)
        {
            seen.LastSeen = time.GetUtcNow();
        }
        else
        {
            networks.Add(db.AccountNetworks.Add(new AccountNetwork { OwnerId = ownerId, Network = network, LastSeen = time.GetUtcNow() }).Entity);
        }

        db.AccountNetworks.RemoveRange(networks.OrderByDescending(n => n.LastSeen).Skip(NetworksKept));
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || HostStore.IsDuplicateKey(ex))
        {
            // The same account signed in twice at once; one record of it is enough.
        }
    }

    /// <summary>Networks two or more accounts used within the window, most recent first.</summary>
    public async Task<IReadOnlyList<SharedNetwork>> GetSharedNetworksAsync()
    {
        var since = time.GetUtcNow() - Window;
        await using var db = await dbs.CreateDbContextAsync();
        var shared = db.AccountNetworks.Where(n => n.LastSeen >= since)
            .GroupBy(n => n.Network)
            .Where(g => g.Count() > 1) // the key is (owner, network), so each row is a different account
            .Select(g => g.Key);
        var uses = await db.AccountNetworks.Where(n => n.LastSeen >= since && shared.Contains(n.Network)).ToListAsync();

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
        await using (var db = await dbs.CreateDbContextAsync())
        {
            if (await db.ServerSecrets.FindAsync(KeyName) is { } existing)
            {
                return existing.Value;
            }

            db.ServerSecrets.Add(new ServerSecret { Name = KeyName, Value = RandomNumberGenerator.GetBytes(32) });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (HostStore.IsDuplicateKey(ex))
            {
                // Another request created it first; use theirs.
            }
        }

        await using var fresh = await dbs.CreateDbContextAsync();
        return (await fresh.ServerSecrets.FindAsync(KeyName))!.Value;
    }
}
