using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Sign-in, sign-out of banned and deleted accounts, rate limits and multi-account signals, over HTTP.</summary>
public sealed partial class SecurityFlowTests : TestDatabase
{
    private const string Merlin = "merlin@spellbound.test";

    private readonly WebApplicationFactory<Program> factory;
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));

    public SecurityFlowTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IStartupFilter>(new ClientAddress(IPAddress.Parse("203.0.113.7")));
        }));
    }

    private AccountSignals Signals => factory.Services.GetRequiredService<AccountSignals>();

    private AccountService Accounts => factory.Services.GetRequiredService<AccountService>();

    [Fact]
    public async Task First_sign_in_creates_the_account_signs_in_and_records_a_network_signal()
    {
        var client = Client();

        var response = await SignInAsync(client, Merlin);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        var id = AccountService.IdFor(AccountEndpoints.DevProvider, Merlin);
        Assert.Equal(Merlin, (await Accounts.FindAsync(id))!.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/Account/Manage", UriKind.Relative))).StatusCode);
        using var db = OpenDb();
        Assert.Single(db.AccountNetworks.Where(n => n.OwnerId == id));
    }

    [Fact]
    public async Task Signing_in_again_finds_the_same_account()
    {
        await SignInAsync(Client(), Merlin);
        await SignInAsync(Client(), "MERLIN@spellbound.test ");

        using var db = OpenDb();
        Assert.Single(db.Accounts);
    }

    [Fact]
    public async Task After_signing_in_only_a_local_page_is_opened()
    {
        Assert.Equal("/rivals", (await SignInAsync(Client(), Merlin, "/rivals")).Headers.Location!.OriginalString);
        Assert.Equal("/", (await SignInAsync(Client(), Merlin, "https://evil.example/")).Headers.Location!.OriginalString);
        Assert.Equal("/", (await SignInAsync(Client(), Merlin, "//evil.example/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_banned_player_cannot_sign_in_and_their_open_session_is_signed_out()
    {
        var client = Client();
        await SignInAsync(client, Merlin);
        var id = AccountService.IdFor(AccountEndpoints.DevProvider, Merlin);
        await factory.Services.GetRequiredService<AdminService>().BanAsync("admin", "admin@spellbound.test", id, "Cheating");

        time.Advance(AccountEndpoints.CheckEvery);
        var page = await client.GetAsync(new Uri("/Account/Manage", UriKind.Relative));
        var again = await SignInAsync(Client(), Merlin);

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("Account/Login", page.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Single(page.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Cookies=;", StringComparison.Ordinal));
        Assert.Contains("banned", Uri.UnescapeDataString(again.Headers.Location!.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sign_in_is_checked_against_the_database_at_most_once_a_minute()
    {
        var client = Client();
        await SignInAsync(client, Merlin); // checked at sign-in
        var manage = new Uri("/Account/Manage", UriKind.Relative);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(manage)).StatusCode); // checked again, cookie rewritten

        var id = AccountService.IdFor(AccountEndpoints.DevProvider, Merlin);
        await factory.Services.GetRequiredService<AdminService>().BanAsync("admin", "admin@spellbound.test", id, "Cheating");
        time.Advance(TimeSpan.FromSeconds(30));
        var withinTheMinute = await client.GetAsync(manage); // the rewritten cookie says it was checked 30 s ago
        time.Advance(TimeSpan.FromSeconds(31));
        var afterIt = await client.GetAsync(manage);

        Assert.Equal(HttpStatusCode.OK, withinTheMinute.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, afterIt.StatusCode);
    }

    [Fact]
    public async Task Deleting_your_account_needs_a_confirmation_then_deletes_it_with_its_signals()
    {
        var client = Client();
        await SignInAsync(client, Merlin);
        var id = AccountService.IdFor(AccountEndpoints.DevProvider, Merlin);

        var unconfirmed = await PostAsync(client, "/Account/Manage", "/Account/Delete", new() { ["confirm"] = "yes" });
        Assert.NotNull(await Accounts.FindAsync(id));
        Assert.Contains("error=confirm", unconfirmed.Headers.Location!.ToString(), StringComparison.Ordinal);

        var deleted = await PostAsync(client, "/Account/Manage", "/Account/Delete", new() { ["confirm"] = "delete" });

        Assert.Contains("deleted=1", deleted.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Null(await Accounts.FindAsync(id));
        using var db = OpenDb();
        Assert.Empty(db.AccountNetworks.Where(n => n.OwnerId == id));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Account/Manage", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Too_many_account_posts_from_one_address_are_refused()
    {
        var client = Client();
        var form = await FormAsync(client, "/Account/Login");

        for (var i = 0; i < 10; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostFormAsync(client, form, "/Account/DevSignIn", Merlin)).StatusCode);
        }

        var refused = await PostFormAsync(client, form, "/Account/DevSignIn", Merlin);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(RateLimits.RejectionMessage, await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Accounts_sharing_a_network_recently_are_listed_together()
    {
        var home = IPAddress.Parse("203.0.113.7");
        await Signals.RecordAsync("account-1", home);
        await Signals.RecordAsync("account-2", home);
        await Signals.RecordAsync("account-3", IPAddress.Parse("198.51.100.20"));

        var shared = Assert.Single(await Signals.GetSharedNetworksAsync());
        Assert.Equal(["account-1", "account-2"], shared.OwnerIds);
        Assert.DoesNotContain("203", shared.Network, StringComparison.Ordinal); // a keyed hash, not the address

        time.Advance(AccountSignals.Window + TimeSpan.FromDays(1));
        Assert.Empty(await Signals.GetSharedNetworksAsync()); // old sightings drop out
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Signs in through the Development sign-in form, so the client carries the sign-in cookie.</summary>
    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string returnUrl = "/") =>
        await PostFormAsync(client, await FormAsync(client, "/Account/Login"), "/Account/DevSignIn", email, returnUrl);

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string page, string action, Dictionary<string, string> fields)
    {
        var form = await FormAsync(client, page);
        foreach (var (name, value) in fields)
        {
            form[name] = value;
        }

        return await client.PostAsync(new Uri(action, UriKind.Relative), new FormUrlEncodedContent(form));
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, Dictionary<string, string> hidden, string action, string email, string returnUrl = "/")
    {
        var fields = new Dictionary<string, string>(hidden) { ["email"] = email, ["returnUrl"] = returnUrl };
        return await client.PostAsync(new Uri(action, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    /// <summary>A page's hidden form fields (the antiforgery token).</summary>
    private static async Task<Dictionary<string, string>> FormAsync(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        return HiddenInput().Matches(html)
            .DistinctBy(m => m.Groups["name"].Value)
            .ToDictionary(m => WebUtility.HtmlDecode(m.Groups["name"].Value), m => WebUtility.HtmlDecode(m.Groups["value"].Value));
    }

    [GeneratedRegex("""<input[^>]*type="hidden"[^>]*name="(?<name>[^"]+)"[^>]*value="(?<value>[^"]*)"[^>]*>""")]
    private static partial Regex HiddenInput();

    /// <summary>TestServer requests have no client address; give them one, as Kestrel would.</summary>
    private sealed class ClientAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
