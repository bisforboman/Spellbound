using System.Net;
using System.Security.Cryptography;
using System.Text;
using Raven.Client.Documents;
using Raven.Client.Exceptions;
using Spellbound.Web.Data;

namespace Spellbound.Web;

/// <summary>Accounts that signed in from the same network recently: a hint for the admin, never proof.</summary>
public sealed record SharedNetwork(string Network, IReadOnlyList<string> OwnerIds, DateTimeOffset LastSeen);

/// <summary>
/// Multi-account signals. At sign-up and sign-in, records a keyed hash of the player's IP address (never the address
/// itself) with the time. The admin page lists networks that several accounts used recently. Households and mobile
/// carriers share addresses, so the admin judges; nothing is banned automatically.
/// </summary>
public sealed class AccountSignals(IDocumentStore store, TimeProvider time)
{
    public const int NetworksKept = 20;

    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private byte[]? key;

    public async Task RecordAsync(string ownerId, IPAddress? address)
    {
        if (address is null)
        {
            return;
        }

        var network = await HashAsync(address);
        using var session = store.OpenAsyncSession();
        var signalKey = RavenSetup.SignalKey(ownerId);
        var signal = await session.LoadAsync<AccountSignal>(signalKey);
        if (signal is null)
        {
            signal = new AccountSignal { OwnerId = ownerId };
            await session.StoreAsync(signal, signalKey);
        }

        signal.Networks[network] = time.GetUtcNow();
        foreach (var old in signal.Networks.OrderByDescending(n => n.Value).Skip(NetworksKept).Select(n => n.Key).ToList())
        {
            signal.Networks.Remove(old);
        }

        await session.SaveChangesAsync();
    }

    /// <summary>Networks two or more accounts used within the window, most recent first.</summary>
    public async Task<IReadOnlyList<SharedNetwork>> GetSharedNetworksAsync()
    {
        var since = time.GetUtcNow() - Window;
        var uses = new List<(string Network, string OwnerId, DateTimeOffset At)>();
        using var session = store.OpenAsyncSession();
        await using var stream = await session.Advanced.StreamAsync<AccountSignal>(startsWith: RavenSetup.SignalPrefix);
        while (await stream.MoveNextAsync())
        {
            var signal = stream.Current.Document;
            uses.AddRange(signal.Networks.Where(n => n.Value >= since).Select(n => (n.Key, signal.OwnerId, n.Value)));
        }

        return
        [
            .. uses.GroupBy(u => u.Network)
                .Where(g => g.Select(u => u.OwnerId).Distinct().Count() > 1)
                .Select(g => new SharedNetwork(g.Key, [.. g.Select(u => u.OwnerId).Distinct().Order()], g.Max(u => u.At)))
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
        using (var session = store.OpenAsyncSession())
        {
            if (await session.LoadAsync<ServerSecrets>(RavenSetup.SecretsKey) is { } existing)
            {
                return existing.SignalKey;
            }

            await session.StoreAsync(
                new ServerSecrets { SignalKey = RandomNumberGenerator.GetBytes(32) }, changeVector: string.Empty, RavenSetup.SecretsKey);
            try
            {
                await session.SaveChangesAsync();
            }
            catch (ConcurrencyException)
            {
                // Another request created it first; use theirs.
            }
        }

        using var fresh = store.OpenAsyncSession();
        return (await fresh.LoadAsync<ServerSecrets>(RavenSetup.SecretsKey)).SignalKey;
    }
}
