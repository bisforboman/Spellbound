using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Turning notifications on through the real app, and sending them through a stand-in push service.</summary>
public sealed class PushFlowTests : TestDatabase
{
    private const string Endpoint = "https://push.example.net/send/merlin-phone";

    private readonly PushService pushService = new();
    private readonly WebApplicationFactory<Program> factory;

    public PushFlowTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
            services.AddHttpClient(PushSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => pushService)));
    }

    [Fact]
    public async Task A_player_turns_notifications_on_and_gets_a_test_one()
    {
        using var client = Client();
        var token = await SignInAsync(client, "merlin@spellbound.test");
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        var key = await client.GetStringAsync(new Uri("/Account/Push/Key", UriKind.Relative));
        var subscribed = await PostAsync(client, "/Account/Push", token, new()
        {
            ["endpoint"] = Endpoint,
            ["p256dh"] = WebPushCrypto.Base64Url(WebPushCrypto.PublicKey(browser)),
            ["auth"] = WebPushCrypto.Base64Url(RandomNumberGenerator.GetBytes(16)),
        });
        var test = await PostAsync(client, "/Account/Push/Test", token, []);

        Assert.Equal(65, WebPushCrypto.FromBase64Url(key).Length); // an uncompressed P-256 key
        Assert.Equal(HttpStatusCode.NoContent, subscribed.StatusCode);
        Assert.Equal("/Account/Manage?push=sent", test.Headers.Location!.ToString());
        var push = await pushService.NextAsync();
        Assert.Equal(Endpoint, push.Url);
        Assert.Equal("aes128gcm", push.Encoding);
        Assert.StartsWith("vapid t=", push.Authorization, StringComparison.Ordinal);
        Assert.EndsWith($"k={key}", push.Authorization, StringComparison.Ordinal);
        Assert.Equal("86400", push.Ttl);
    }

    [Fact]
    public async Task A_subscription_the_push_service_no_longer_knows_is_forgotten()
    {
        await SubscribeAsync("owner-1", Endpoint);
        pushService.Answer = HttpStatusCode.Gone;

        factory.Services.GetRequiredService<PushSink>().Send(new PushMessage("owner-1", "Hi", "Test", "/"));
        await pushService.NextAsync();

        await WaitUntilAsync(async () => await CountAsync("SELECT count(*) FROM push_subscriptions") == 0);
    }

    [Fact]
    public async Task A_player_without_notifications_gets_none_and_nothing_fails()
    {
        await SubscribeAsync("owner-1", Endpoint);

        factory.Services.GetRequiredService<PushSink>().Send(new PushMessage("someone-else", "Hi", "Test", "/"));
        factory.Services.GetRequiredService<PushSink>().Send(new PushMessage("owner-1", "Hi", "Test", "/"));

        Assert.Equal(Endpoint, (await pushService.NextAsync()).Url); // only owner-1's
        Assert.False(pushService.HasMore);
    }

    [Fact]
    public async Task Subscriptions_must_be_https_push_addresses()
    {
        var store = factory.Services.GetRequiredService<PushStore>();

        Assert.Equal("That isn't a push subscription.", await store.SubscribeAsync("owner-1", "http://insecure.example/x", "key", "auth"));
        Assert.Equal("That isn't a push subscription.", await store.SubscribeAsync("owner-1", "not a url", "key", "auth"));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM push_subscriptions"));
    }

    [Fact]
    public async Task A_garbage_key_is_refused()
    {
        var store = factory.Services.GetRequiredService<PushStore>();

        Assert.Equal("That isn't a push subscription.", await store.SubscribeAsync("owner-1", Endpoint, "not-a-key", WebPushCrypto.Base64Url(new byte[16])));
        Assert.Equal("That isn't a push subscription.", await store.SubscribeAsync("owner-1", Endpoint, WebPushCrypto.Base64Url(new byte[65]), "short"));
    }

    [Fact]
    public async Task A_device_that_fails_does_not_stop_the_players_other_devices()
    {
        await SubscribeAsync("owner-1", Endpoint);
        await WithDbAsync(db => db.ExecuteAsync( // a row that slipped in broken, as if stored before keys were checked
            "INSERT INTO push_subscriptions (endpoint, owner_id, p256dh, auth, created_at) VALUES ('https://push.example.net/broken', 'owner-1', 'bad', 'bad', now() - interval '1 day')"));

        factory.Services.GetRequiredService<PushSink>().Send(new PushMessage("owner-1", "Hi", "Test", "/"));

        Assert.Equal(Endpoint, (await pushService.NextAsync()).Url);
    }

    [Fact]
    public async Task Deleting_an_account_deletes_its_subscriptions()
    {
        var accounts = factory.Services.GetRequiredService<AccountService>();
        var id = (await accounts.SignInAsync(AccountEndpoints.DevProvider, "ada@spellbound.test", "ada@spellbound.test")).Account!.Id;
        await SubscribeAsync(id, Endpoint);

        await accounts.DeleteAsync(id);

        Assert.Equal(0, await CountAsync("SELECT count(*) FROM push_subscriptions"));
    }

    [Fact]
    public async Task The_service_worker_and_script_are_served_at_their_plain_addresses()
    {
        using var client = factory.CreateClient();

        Assert.Contains("showNotification", await client.GetStringAsync(new Uri("/sw.js", UriKind.Relative)), StringComparison.Ordinal);
        Assert.Contains("pushManager.subscribe", await client.GetStringAsync(new Uri("/push.js", UriKind.Relative)), StringComparison.Ordinal);
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private async Task SubscribeAsync(string ownerId, string endpoint)
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Assert.Null(await factory.Services.GetRequiredService<PushStore>().SubscribeAsync(
            ownerId, endpoint, WebPushCrypto.Base64Url(WebPushCrypto.PublicKey(browser)), WebPushCrypto.Base64Url(RandomNumberGenerator.GetBytes(16))));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100 && !await condition(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(await condition());
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Signs in with the Development form and returns an antiforgery token from the Account page.</summary>
    private static async Task<string> SignInAsync(HttpClient client, string email)
    {
        var token = Token(await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative)));
        var response = await PostAsync(client, "/Account/DevSignIn", token, new() { ["email"] = email, ["returnUrl"] = "/" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Token(await client.GetStringAsync(new Uri("/Account/Manage", UriKind.Relative)));
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;
        return client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private static string Token(string page) =>
        WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<v>[^\"]+)\"").Groups["v"].Value);

    private sealed record Push(string Url, string? Encoding, string? Authorization, string? Ttl);

    /// <summary>Stands in for a browser's push service: remembers each request and answers as told.</summary>
    private sealed class PushService : HttpMessageHandler
    {
        private readonly Channel<Push> pushes = Channel.CreateUnbounded<Push>();

        public HttpStatusCode Answer { get; set; } = HttpStatusCode.Created;

        public bool HasMore => pushes.Reader.TryPeek(out _);

        public async Task<Push> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await pushes.Reader.ReadAsync(timeout.Token);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await pushes.Writer.WriteAsync(
                new Push(
                    request.RequestUri!.ToString(),
                    request.Content?.Headers.ContentEncoding.FirstOrDefault(),
                    request.Headers.TryGetValues("Authorization", out var auth) ? auth.First() : null,
                    request.Headers.TryGetValues("TTL", out var ttl) ? ttl.First() : null),
                cancellationToken);
            return new HttpResponseMessage(Answer);
        }
    }
}
