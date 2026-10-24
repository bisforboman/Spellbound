using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Spellbound.Domain;
using Spellbound.Web.Components.Game;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>
/// A match's rounds through the real app on a real PostgreSQL: the player's actions go through the same service the
/// match page calls, and the page is read back over HTTP with the player's sign-in cookie, as a phone would get it.
/// </summary>
public sealed class RoundFlowTests : TestDatabase, IAsyncLifetime
{
    private const string Merlin = "merlin@spellbound.test";
    private const string Morgana = "morgana@spellbound.test";

    private readonly WebApplicationFactory<Program> factory;
    private string merlinId = string.Empty;

    public RoundFlowTests()
    {
        factory = CreateApp();
    }

    private MatchPlay Play => factory.Services.GetRequiredService<MatchPlay>();

    private AccountService Accounts => factory.Services.GetRequiredService<AccountService>();

    public async Task InitializeAsync()
    {
        merlinId = (await Accounts.SignInAsync(AccountEndpoints.DevProvider, Merlin, Merlin)).Account!.Id;
        await Accounts.SignInAsync(AccountEndpoints.DevProvider, Morgana, Morgana);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_round_is_committed_saved_and_shown_on_the_match_page()
    {
        using var merlin = Client();
        await SignInAsync(merlin, Merlin);
        var id = await ToTheRoundsAsync();
        var home = (await Play.ViewAsync(id, merlinId))!.Match.Seats[0].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).First();

        var report = await Play.OrdersAsync(id, merlinId, Orders.Summon(0, home, UnitKind.Imp, 2));
        var page = await merlin.GetStringAsync(new Uri($"/match/{id}", UriKind.Relative));
        var saved = (await Play.ViewAsync(id, merlinId))!;

        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        Assert.Equal(2, saved.Match.CurrentRound);
        Assert.Equal(new Army(2, 0), saved.Match.UnitsOn(home));
        Assert.Equal(report.Lines, saved.LastReport);
        Assert.Contains("Round 2 of 8", page, StringComparison.Ordinal);
        Assert.Contains($"You summon 2 Imps at {TileNames.Of(saved.Match.Map, home)}", page, StringComparison.Ordinal); // in your words, by tile name
        Assert.Contains($"href=\"match/{id}/help\"", page, StringComparison.Ordinal); // the trait's goal and the rules
        Assert.Contains($"href=\"match/{id}#report\"", page, StringComparison.Ordinal); // the view tabs
    }

    [Fact]
    public async Task Orders_that_fail_change_nothing_in_the_database()
    {
        var id = await ToTheRoundsAsync();
        var before = await WithDbAsync(db => db.LoadGameStateAsync(id));

        var tooMany = await Play.OrdersAsync(id, merlinId, Orders.Summon(0, (await Play.ViewAsync(id, merlinId))!.Match.Seats[0].Tiles.First(), UnitKind.Golem, 1000));

        Assert.False(tooMany.Succeeded);
        Assert.Equal(before, await WithDbAsync(db => db.LoadGameStateAsync(id)));
    }

    [Fact]
    public async Task A_double_tap_waits_for_the_first_and_each_tap_is_a_whole_round()
    {
        var id = await ToTheRoundsAsync();

        var taps = await Task.WhenAll(Play.OrdersAsync(id, merlinId, Orders.Hold(0)), Play.OrdersAsync(id, merlinId, Orders.Hold(0)));
        var saved = (await Play.ViewAsync(id, merlinId))!;

        // The player's row is locked, so the second tap waits and plays the next round; neither sees half a round.
        Assert.All(taps, t => Assert.True(t.Succeeded));
        Assert.Equal(3, saved.Match.CurrentRound);
        Assert.Equal(["Round 1.", "Round 2."], taps.Select(t => t.Lines[0]).Order());
    }

    [Fact]
    public async Task The_last_round_ends_the_game_for_the_host_and_the_page_shows_the_end()
    {
        using var merlin = Client();
        await SignInAsync(merlin, Merlin);
        var id = await ToTheRoundsAsync();

        for (var round = 1; round <= GameRules.Default.Rounds; round++)
        {
            Assert.True((await Play.OrdersAsync(id, merlinId, Orders.Hold(0))).Succeeded);
        }

        var game = await WithDbAsync(db => db.LoadGameAsync(id));
        var page = await merlin.GetStringAsync(new Uri($"/match/{id}", UriKind.Relative));
        var after = await Play.OrdersAsync(id, merlinId, Orders.Hold(0));

        Assert.NotNull(game!.EndedAt);
        Assert.Contains("The match is over", page, StringComparison.Ordinal);
        Assert.Contains("trait,", page, StringComparison.Ordinal); // every goal in the end report
        Assert.False(after.Succeeded);
    }

    [Fact]
    public async Task Only_the_player_in_the_match_sees_its_page()
    {
        using var morgana = Client();
        using var nobody = Client();
        await SignInAsync(morgana, Morgana);
        var id = await ToTheRoundsAsync();

        var other = await morgana.GetStringAsync(new Uri($"/match/{id}", UriKind.Relative));
        var signedOut = await nobody.GetAsync(new Uri($"/match/{id}", UriKind.Relative));

        Assert.Contains("Not found", other, StringComparison.Ordinal);
        Assert.DoesNotContain("Your trait", other, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Contains("Account/Login", signedOut.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A 3-seat match for Merlin against bots, his trait drafted and the opening passed, in round 1.</summary>
    private async Task<long> ToTheRoundsAsync()
    {
        var id = await Play.StartAsync(merlinId, 3, GameRules.Default);
        var offer = (await Play.ViewAsync(id, merlinId))!.Match.Seats[0].Offer;
        Assert.True((await Play.TraitAsync(id, merlinId, offer[0])).Succeeded);
        await Opening.PassAsync(Play, id, merlinId);

        Assert.Equal(MatchPhase.Rounds, (await Play.ViewAsync(id, merlinId))!.Match.Phase);
        return id;
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Signs in through the Development sign-in form, so the client carries the sign-in cookie.</summary>
    private static async Task SignInAsync(HttpClient client, string email)
    {
        var login = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        var token = System.Text.RegularExpressions.Regex.Match(
            login, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<v>[^\"]+)\"").Groups["v"].Value;
        var response = await client.PostAsync(
            new Uri("/Account/DevSignIn", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
                ["email"] = email,
                ["returnUrl"] = "/",
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }
}
