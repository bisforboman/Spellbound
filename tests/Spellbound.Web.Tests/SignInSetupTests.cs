using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Spellbound.Web.Tests;

/// <summary>Outside Development, Google sign-in must be configured, and the Development sign-in must not exist.</summary>
public sealed class SignInSetupTests : TestDatabase
{
    [Fact]
    public void Production_without_a_Google_client_refuses_to_start()
    {
        using var app = CreateApp(b => b.UseEnvironment("Production"));

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("Authentication__Google__ClientId", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_offers_Google_and_has_no_development_sign_in()
    {
        using var app = CreateApp(b => b
            .UseEnvironment("Production")
            .UseSetting("Authentication:Google:ClientId", "test-client")
            .UseSetting("Authentication:Google:ClientSecret", "test-secret"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var login = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        // With a valid antiforgery token (from the Google form), so only a missing endpoint can refuse the sign-in.
        var token = Regex.Match(login, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<v>[^\"]+)\"").Groups["v"].Value;
        var dev = await client.PostAsync(
            new Uri("/Account/DevSignIn", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
                ["email"] = "merlin@spellbound.test",
            }));

        Assert.Contains("Sign in with Google", login, StringComparison.Ordinal);
        Assert.DoesNotContain("DevSignIn", login, StringComparison.Ordinal);
        Assert.NotEmpty(token);
        Assert.NotEqual(HttpStatusCode.Redirect, dev.StatusCode); // a sign-in would redirect
        Assert.False(dev.Headers.Contains("Set-Cookie") && dev.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith(".AspNetCore.Cookies", StringComparison.Ordinal)));
        using var db = OpenDb();
        Assert.Empty(db.Accounts);

        var google = await client.PostAsync(
            new Uri("/Account/ExternalLogin", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
                ["returnUrl"] = "/rivals",
            }));
        Assert.Equal(HttpStatusCode.Redirect, google.StatusCode);
        Assert.StartsWith("https://accounts.google.com/", google.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains("client_id=test-client", google.Headers.Location!.Query, StringComparison.Ordinal);
    }
}
