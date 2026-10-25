using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Spellbound.Web.Tests;

/// <summary>The page looks: picked with /look/{name}, kept in a cookie per device, set on every page's html.</summary>
public sealed class LookTests : TestDatabase
{
    private readonly WebApplicationFactory<Program> factory;

    public LookTests() => factory = CreateApp();

    [Fact]
    public async Task Picking_a_look_keeps_it_for_a_year_and_draws_pages_in_it()
    {
        var client = Client();

        var response = await client.GetAsync(new Uri("/look/slate?back=/privacy", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/privacy", response.Headers.Location!.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("look=slate;", cookie, StringComparison.Ordinal);
        Assert.Contains("max-age=31536000", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var page = new HttpRequestMessage(HttpMethod.Get, new Uri("/privacy", UriKind.Relative));
        page.Headers.Add("Cookie", "look=slate");
        Assert.Contains("data-look=\"slate\"", await (await client.SendAsync(page)).Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_look_changes_nothing()
    {
        var response = await Client().GetAsync(new Uri("/look/neon", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Pages_without_a_look_use_the_grimoire_and_a_bad_cookie_is_ignored()
    {
        var page = new HttpRequestMessage(HttpMethod.Get, new Uri("/privacy", UriKind.Relative));
        page.Headers.Add("Cookie", "look=\"><script>");

        var html = await (await Client().SendAsync(page)).Content.ReadAsStringAsync();

        Assert.Contains("data-look=\"grimoire\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_only_sends_the_player_back_within_the_site()
    {
        var response = await Client().GetAsync(new Uri("/look/dust?back=//elsewhere.test/", UriKind.Relative));

        Assert.Equal("/Account/Manage", response.Headers.Location!.OriginalString);
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
}
