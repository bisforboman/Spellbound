using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace Spellbound.Web.Hosting;

/// <summary>A notification for one player, shown on every device where they turned notifications on.</summary>
/// <param name="Url">The page to open when it's tapped, relative to the site.</param>
public sealed record PushMessage(string OwnerId, string Title, string Body, string Url);

/// <summary>
/// Where the game hands notifications to send. Sending happens in the background (<see cref="PushSender"/>), so an
/// action never waits on a push service; hand them over only after the action's transaction has committed.
/// </summary>
public sealed class PushSink
{
    private readonly Channel<PushMessage> outbox = Channel.CreateBounded<PushMessage>(
        new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<PushMessage> Outbox => outbox.Reader;

    public void Send(PushMessage message) => outbox.Writer.TryWrite(message);
}

/// <summary>A browser where a player turned notifications on.</summary>
public sealed record PushSubscription(string Endpoint, string OwnerId, string P256dh, string Auth);

/// <summary>
/// Players' push subscriptions and this server's VAPID key, which identifies it to browsers' push services. The key is
/// made once and kept: every subscription is bound to it.
/// </summary>
public sealed class PushStore(HostDatabase database, TimeProvider time)
{
    private const string KeyName = "vapid-key";

    private byte[]? keyDer;

    /// <summary>The server's VAPID public key, for the browser to subscribe with (base64url).</summary>
    public async Task<string> PublicKeyAsync()
    {
        using var key = await KeyAsync();
        return WebPushCrypto.Base64Url(WebPushCrypto.PublicKey(key));
    }

    public async Task<ECDsa> KeyAsync()
    {
        if (keyDer is null)
        {
            using var fresh = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            await using var db = await database.BeginAsync();
            await db.ExecuteAsync(
                "INSERT INTO server_secrets (name, value) VALUES ($1, $2) ON CONFLICT (name) DO NOTHING",
                KeyName, fresh.ExportECPrivateKey()); // the first use makes it; later ones keep it
            keyDer = (await db.ScalarAsync<byte[]>("SELECT value FROM server_secrets WHERE name = $1", KeyName))!;
            await db.CommitAsync();
        }

        var key = ECDsa.Create();
        key.ImportECPrivateKey(keyDer, out _);
        return key;
    }

    /// <summary>Saves a browser's subscription for the player (a browser re-subscribing replaces its old row).</summary>
    public async Task<string?> SubscribeAsync(string ownerId, string endpoint, string p256dh, string auth)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || p256dh.Length is 0 or > 200 || auth.Length is 0 or > 100 || endpoint.Length > 2_000
            || !WebPushCrypto.IsBrowserKey(p256dh, auth))
        {
            return "That isn't a push subscription.";
        }

        await using var db = await database.BeginAsync();
        await db.ExecuteAsync(
            """
            INSERT INTO push_subscriptions (endpoint, owner_id, p256dh, auth, created_at) VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (endpoint) DO UPDATE SET owner_id = excluded.owner_id, p256dh = excluded.p256dh, auth = excluded.auth
            """,
            endpoint, ownerId, p256dh, auth, time.GetUtcNow());
        await db.CommitAsync();
        return null;
    }

    public async Task UnsubscribeAsync(string ownerId, string endpoint)
    {
        await using var db = await database.BeginAsync();
        await db.ExecuteAsync("DELETE FROM push_subscriptions WHERE owner_id = $1 AND endpoint = $2", ownerId, endpoint);
        await db.CommitAsync();
    }

    public async Task<IReadOnlyList<PushSubscription>> ForOwnerAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        return await db.QueryAsync(
            "SELECT endpoint, owner_id, p256dh, auth FROM push_subscriptions WHERE owner_id = $1 ORDER BY created_at",
            r => new PushSubscription(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)),
            ownerId);
    }

    /// <summary>Forgets a subscription the push service says is gone.</summary>
    public async Task ForgetAsync(string endpoint)
    {
        await using var db = await database.BeginAsync();
        await db.ExecuteAsync("DELETE FROM push_subscriptions WHERE endpoint = $1", endpoint);
        await db.CommitAsync();
    }
}

/// <summary>
/// Sends queued notifications to each of the player's browsers through their push services: encrypted so only the
/// browser can read them (RFC 8291), signed with the server's VAPID key (RFC 8292). A subscription the push service no
/// longer knows is forgotten. Failures are warnings: they're about players' browsers, not the server.
/// </summary>
public sealed class PushSender(PushSink sink, PushStore store, IHttpClientFactory http, IConfiguration configuration, TimeProvider time, ILogger<PushSender> logger)
    : BackgroundService
{
    public const string HttpClientName = "push";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in sink.Outbox.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't send a notification.");
            }
        }
    }

    private async Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        var subscriptions = await store.ForOwnerAsync(message.OwnerId);
        if (subscriptions.Count == 0)
        {
            return;
        }

        using var key = await store.KeyAsync();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { title = message.Title, body = message.Body, url = message.Url });
        using var client = http.CreateClient(HttpClientName);
        foreach (var subscription in subscriptions)
        {
            try
            {
                await SendAsync(client, key, subscription, payload, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't send a notification to one of a player's devices."); // the others still get it
            }
        }
    }

    private async Task SendAsync(HttpClient client, ECDsa key, PushSubscription subscription, byte[] payload, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(subscription.Endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(WebPushCrypto.Encrypt(
                WebPushCrypto.FromBase64Url(subscription.P256dh), WebPushCrypto.FromBase64Url(subscription.Auth), payload)),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("TTL", "86400");
        request.Headers.TryAddWithoutValidation("Authorization", WebPushCrypto.VapidAuthorization(endpoint, key, Subject(), time.GetUtcNow()));
        using var response = await client.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode is 404 or 410)
        {
            await store.ForgetAsync(subscription.Endpoint); // gone: the player turned it off, or the browser forgot it
        }
        else if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("A push service answered {Status} for a notification.", (int)response.StatusCode);
        }
    }

    /// <summary>Who runs this server, for push services to contact: the privacy notice's contact, as a mailto or https address.</summary>
    private string Subject() => configuration["Privacy:Contact"]?.Trim() is { Length: > 0 } contact
        ? contact.StartsWith("https://", StringComparison.Ordinal) || contact.StartsWith("mailto:", StringComparison.Ordinal)
            ? contact
            : contact.Contains('@', StringComparison.Ordinal) ? $"mailto:{contact}" : "mailto:admin@localhost"
        : "mailto:admin@localhost";
}
