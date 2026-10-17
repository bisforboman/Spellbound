using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Rate limits, login lockout and multi-account signals, through the real login form.</summary>
public sealed partial class SecurityFlowTests : TestDatabase
{
    private const string Password = "Test-Only-1!";

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

    [Fact]
    public async Task Successful_login_records_a_network_signal()
    {
        var id = await CreateUserAsync("merlin@spellbound.test");

        var response = await LoginAsync(Client(), "merlin@spellbound.test", Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = OpenDb();
        Assert.Single(db.AccountNetworks.Where(n => n.OwnerId == id));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        await CreateUserAsync("merlin@spellbound.test");
        var client = Client();

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(client, "merlin@spellbound.test", "wrong-password");
        }

        var response = await LoginAsync(client, "merlin@spellbound.test", Password); // right password, but locked

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Account/Lockout", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_account_posts_from_one_address_are_refused()
    {
        var client = Client();
        var form = await GetFormAsync(client);

        for (var i = 0; i < 10; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostLoginAsync(client, form, "nobody@spellbound.test", "x")).StatusCode);
        }

        var refused = await PostLoginAsync(client, form, "nobody@spellbound.test", "x");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(RateLimits.RejectionMessage, await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Accounts_sharing_a_network_recently_are_listed_together()
    {
        var home = IPAddress.Parse("203.0.113.7");
        await Signals.RecordAsync("ApplicationUsers/1-A", home);
        await Signals.RecordAsync("ApplicationUsers/2-A", home);
        await Signals.RecordAsync("ApplicationUsers/3-A", IPAddress.Parse("198.51.100.20"));

        var shared = Assert.Single(await Signals.GetSharedNetworksAsync());
        Assert.Equal(["ApplicationUsers/1-A", "ApplicationUsers/2-A"], shared.OwnerIds);
        Assert.DoesNotContain("203", shared.Network, StringComparison.Ordinal); // a keyed hash, not the address

        time.Advance(AccountSignals.Window + TimeSpan.FromDays(1));
        Assert.Empty(await Signals.GetSharedNetworksAsync()); // old sightings drop out
    }

    [Fact]
    public async Task Deleting_an_account_deletes_its_signals()
    {
        var id = await CreateUserAsync("merlin@spellbound.test");
        await Signals.RecordAsync(id, IPAddress.Parse("203.0.113.7"));

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByIdAsync(id))!)).Succeeded);
        }

        using var db = OpenDb();
        Assert.Empty(db.AccountNetworks.Where(n => n.OwnerId == id));
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

    private async Task<string> CreateUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        return user.Id!;
    }

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await PostLoginAsync(client, await GetFormAsync(client), email, password);

    /// <summary>The login page's hidden form fields (antiforgery token and form name).</summary>
    private static async Task<Dictionary<string, string>> GetFormAsync(HttpClient client)
    {
        var html = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        return HiddenInput().Matches(html).ToDictionary(m => WebUtility.HtmlDecode(m.Groups["name"].Value), m => WebUtility.HtmlDecode(m.Groups["value"].Value));
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client, Dictionary<string, string> hidden, string email, string password)
    {
        var fields = new Dictionary<string, string>(hidden)
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        };
        return await client.PostAsync(new Uri("/Account/Login", UriKind.Relative), new FormUrlEncodedContent(fields));
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
