using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Domain;
using Spellbound.Domain.Skirmish;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Skirmish games through the real app: orders, ready-up, the round's time limit, replays and accounts.</summary>
public sealed class SkirmishFlowTests : TestDatabase, IAsyncLifetime
{
    private static readonly TimeSpan RoundLength = TimeSpan.FromHours(1);

    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> factory;
    private string merlin = string.Empty;
    private string morgana = string.Empty;
    private string ada = string.Empty;
    private long gameId;

    public SkirmishFlowTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
            services.RemoveAll<IRandomSource>();
            services.AddSingleton<IRandomSource>(new SeededRandom(5));

            // Keep notifications in the queue, where the tests read them, instead of sending them.
            services.Remove(services.Single(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && d.ImplementationType == typeof(PushSender)));
        }));
    }

    private SkirmishService Skirmishes => factory.Services.GetRequiredService<SkirmishService>();

    private AccountService Accounts => factory.Services.GetRequiredService<AccountService>();

    private SkirmishRecordService Records => factory.Services.GetRequiredService<SkirmishRecordService>();

    /// <summary>The notifications queued so far, taken from the queue.</summary>
    private List<PushMessage> Notifications()
    {
        var reader = factory.Services.GetRequiredService<PushSink>().Outbox;
        var taken = new List<PushMessage>();
        while (reader.TryRead(out var message))
        {
            taken.Add(message);
        }

        return taken;
    }

    public async Task InitializeAsync()
    {
        merlin = await CreateUserAsync("merlin@spellbound.test");
        morgana = await CreateUserAsync("morgana@spellbound.test");
        ada = await CreateUserAsync("ada@spellbound.test");
        gameId = await Skirmishes.CreateAsync(
            [(merlin, "Merlin", MagicSchool.A), (morgana, "Morgana", MagicSchool.B), (ada, "Ada", MagicSchool.C)],
            SkirmishRules.Default,
            RoundLength);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_new_game_shows_each_player_their_seat_and_the_first_round()
    {
        var view = (await Skirmishes.GetAsync(gameId, morgana))!;

        Assert.Equal(1, view.Seat);
        Assert.Equal(1, view.Planning);
        Assert.Equal(3, view.Players);
        Assert.Equal(time.GetUtcNow() + RoundLength, view.RoundEndsAt);
        Assert.Null(view.LastReport);
        Assert.Null(await Skirmishes.GetAsync(gameId, "someone-else"));
        Assert.Equal("Morgana", Assert.Single(await Skirmishes.GetMineAsync(morgana)).SeatName);
        Assert.Equal(SkirmishService.Mode, (await WithDbAsync(db => db.LoadGameAsync(gameId)))!.Mode);
    }

    [Fact]
    public async Task The_round_plays_when_the_last_player_readies_up()
    {
        var claim = await ClaimableAsync(merlin);
        Assert.Null(await Skirmishes.SaveOrdersAsync(gameId, merlin, 1, [new ClaimOrder(claim)]));
        Assert.Null(await Skirmishes.SetReadyAsync(gameId, merlin, 1, true));
        Assert.Null(await Skirmishes.SetReadyAsync(gameId, morgana, 1, true));
        Assert.Equal(1, (await Skirmishes.GetAsync(gameId, merlin))!.Planning); // still waiting for Ada

        Assert.Null(await Skirmishes.SetReadyAsync(gameId, ada, 1, true));

        var view = (await Skirmishes.GetAsync(gameId, merlin))!;
        Assert.Equal(2, view.Planning);
        Assert.Equal(0, view.Game.Board[claim].Owner);
        Assert.Empty(view.Orders); // a new round starts with no orders
        Assert.False(view.Ready);
        Assert.Contains(view.LastReport!.Public, l => l.StartsWith("Merlin claimed", StringComparison.Ordinal));
        Assert.Equal(time.GetUtcNow() + RoundLength, view.RoundEndsAt);
    }

    [Fact]
    public async Task Two_last_ready_taps_at_once_play_the_round_once()
    {
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);

        var results = await Task.WhenAll(
            Task.Run(() => Skirmishes.SetReadyAsync(gameId, morgana, 1, true)),
            Task.Run(() => Skirmishes.SetReadyAsync(gameId, ada, 1, true)));

        Assert.All(results, Assert.Null);
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM skirmish_rounds WHERE game_id = $1", gameId));
        Assert.Equal(2, (await Skirmishes.GetAsync(gameId, ada))!.Planning);
    }

    [Fact]
    public async Task Orders_for_a_round_that_has_played_are_refused_and_change_nothing()
    {
        await PlayRoundAsync();
        var before = (await Skirmishes.GetAsync(gameId, merlin))!.Orders;

        var result = await Skirmishes.SaveOrdersAsync(gameId, merlin, 1, [new RecruitOrder("unit-1", 1)]);

        Assert.Equal("That round has already played. Have a look at the new one and try again.", result);
        Assert.Equal(before, (await Skirmishes.GetAsync(gameId, merlin))!.Orders);
    }

    [Fact]
    public async Task Orders_the_rules_refuse_are_not_saved()
    {
        var result = await Skirmishes.SaveOrdersAsync(gameId, merlin, 1, [new AttackOrder(0)]);

        Assert.NotNull(result);
        Assert.Empty((await Skirmishes.GetAsync(gameId, merlin))!.Orders);
    }

    [Fact]
    public async Task When_time_runs_out_the_round_plays_with_the_saved_orders()
    {
        await Skirmishes.SaveOrdersAsync(gameId, morgana, 1, [new RecruitOrder("unit-1", 5)]);
        Assert.Equal(0, await Skirmishes.PlayDueAsync()); // not yet

        time.Advance(RoundLength);
        Assert.Equal(1, await Skirmishes.PlayDueAsync());

        var view = (await Skirmishes.GetAsync(gameId, morgana))!;
        Assert.Equal(2, view.Planning);
        Assert.Equal(5, view.Game.Seats[1].Units);
        Assert.Equal(1, view.Game.Seats[0].MissedInARow); // nobody readied up
        Assert.Equal(0, await Skirmishes.PlayDueAsync());
    }

    [Fact]
    public async Task A_played_round_replays_exactly_from_what_was_stored()
    {
        await Skirmishes.SaveOrdersAsync(gameId, merlin, 1, [new ClaimOrder(await ClaimableAsync(merlin)), new RecruitOrder("unit-3", 10)]);
        await Skirmishes.SaveOrdersAsync(gameId, morgana, 1, [new HexOrder("hex-b", 2)]);
        await PlayRoundAsync();

        var stored = await WithDbAsync(db => db.SingleAsync(
            """
            SELECT k.orders::text, k.ready::text, k.draws::text, k.state_after::text, g.seed
            FROM skirmish_rounds k JOIN skirmish_games g ON g.game_id = k.game_id WHERE k.game_id = $1 AND k.round = 1
            """,
            r => new { Orders = r.GetString(0), Ready = r.GetString(1), Draws = r.GetString(2), After = r.GetString(3), Seed = r.GetInt32(4) },
            gameId));
        var before = SkirmishGame.Start(
            [("Merlin", MagicSchool.A), ("Morgana", MagicSchool.B), ("Ada", MagicSchool.C)], SkirmishRules.Default, new SeededRandom(stored!.Seed));
        var orders = DomainJson.Deserialize<Dictionary<int, List<SkirmishOrder>>>(stored.Orders)
            .ToDictionary(o => o.Key, o => (IReadOnlyList<SkirmishOrder>)o.Value);

        before.PlayRound(orders, new Replayed(DomainJson.Deserialize<List<double>>(stored.Draws)), DomainJson.Deserialize<HashSet<int>>(stored.Ready));

        // jsonb orders keys its own way, so compare the games, not the text.
        Assert.Equal(DomainJson.Serialize(DomainJson.Deserialize<SkirmishGame>(stored.After)), DomainJson.Serialize(before));
    }

    [Fact]
    public async Task The_last_round_ends_the_game()
    {
        var shortGame = await Skirmishes.CreateAsync(
            [(merlin, "Merlin", MagicSchool.A), (morgana, "Morgana", MagicSchool.B)], SkirmishRules.Default with { Rounds = 1 }, RoundLength);

        await Skirmishes.SetReadyAsync(shortGame, merlin, 1, true);
        await Skirmishes.SetReadyAsync(shortGame, morgana, 1, true);

        Assert.NotNull(await WithDbAsync(db => db.ScalarAsync<DateTimeOffset?>("SELECT ended_at FROM games WHERE id = $1", shortGame)));
        Assert.Equal("The game is over.", await Skirmishes.SetReadyAsync(shortGame, merlin, 2, true));
        var summary = (await Skirmishes.GetMineAsync(merlin)).Single(s => s.GameId == shortGame);
        Assert.True(summary.Ended);
        Assert.Equal(0, await Skirmishes.PlayDueAsync());
    }

    [Fact]
    public async Task A_finished_game_goes_on_everyones_record_with_one_winner()
    {
        var shortGame = await Skirmishes.CreateAsync(
            [(merlin, "Merlin", MagicSchool.A), (morgana, "Morgana", MagicSchool.B), (ada, "Ada", MagicSchool.C)],
            SkirmishRules.Default with { Rounds = 1 },
            RoundLength);
        await PlayRoundAsync(); // the other game's round: it isn't over, so it records nothing
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM skirmish_results"));

        foreach (var player in new[] { merlin, morgana, ada })
        {
            await Skirmishes.SetReadyAsync(shortGame, player, 1, true);
        }

        var winner = (await Skirmishes.GetAsync(shortGame, merlin))!.Game.Standings()[0].Name;
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM skirmish_results WHERE won"));
        Assert.Equal(3, await CountAsync("SELECT count(*) FROM skirmish_results WHERE game_id = $1", shortGame));
        var leaders = await Records.GetLeadersAsync();
        Assert.Equal(winner, leaders[0].Name);
        Assert.Equal([1, 0, 0], leaders.Select(l => l.Won));
        Assert.All(leaders, l => Assert.Equal(1, l.Played));
        Assert.Equal(new SkirmishRecord(1, leaders.Single(l => l.OwnerId == morgana).Won), await Records.GetAsync(morgana));
    }

    [Fact]
    public async Task A_player_who_dropped_out_still_gets_a_game_played()
    {
        var shortGame = await Skirmishes.CreateAsync(
            [(merlin, "Merlin", MagicSchool.A), (morgana, "Morgana", MagicSchool.B)],
            SkirmishRules.Default with { Rounds = 2, AwayAfterMissed = 1, OutAfterMissed = 1 },
            RoundLength);
        await Skirmishes.SetReadyAsync(shortGame, merlin, 1, true);
        time.Advance(RoundLength); // Morgana never readies: the clock plays round 1, and she's out
        await Skirmishes.PlayDueAsync();
        await Skirmishes.SetReadyAsync(shortGame, merlin, 2, true); // the round waits only for players still in

        Assert.True((await Skirmishes.GetAsync(shortGame, merlin))!.Game.Ended);
        Assert.Equal(new SkirmishRecord(1, 0), await Records.GetAsync(morgana));
        Assert.Equal(new SkirmishRecord(1, 1), await Records.GetAsync(merlin));
    }

    [Fact]
    public async Task Banned_players_are_left_off_the_leaderboard_and_deleted_accounts_lose_their_record()
    {
        var shortGame = await Skirmishes.CreateAsync(
            [(merlin, "Merlin", MagicSchool.A), (morgana, "Morgana", MagicSchool.B), (ada, "Ada", MagicSchool.C)],
            SkirmishRules.Default with { Rounds = 1 },
            RoundLength);
        foreach (var player in new[] { merlin, morgana, ada })
        {
            await Skirmishes.SetReadyAsync(shortGame, player, 1, true);
        }

        await WithDbAsync(db => db.ExecuteAsync("INSERT INTO bans (owner_id, at, reason, banned_by) VALUES ($1, now(), 'test', 'admin')", ada));
        await Accounts.DeleteAsync(morgana);

        Assert.Equal([merlin], (await Records.GetLeadersAsync()).Select(l => l.OwnerId));
        Assert.Equal(new SkirmishRecord(0, 0), await Records.GetAsync(morgana));
        Assert.Equal(1, (await Records.GetAsync(ada)).Played); // kept, only hidden
    }

    [Fact]
    public async Task A_banned_player_cannot_act_in_a_skirmish()
    {
        await WithDbAsync(db => db.ExecuteAsync("INSERT INTO bans (owner_id, at, reason, banned_by) VALUES ($1, now(), 'test', 'admin')", ada));

        Assert.Equal("Your account is banned.", await Skirmishes.SetReadyAsync(gameId, ada, 1, true));
        Assert.Equal("Your account is banned.", await Skirmishes.SaveOrdersAsync(gameId, ada, 1, []));
    }

    [Fact]
    public async Task A_deleted_account_leaves_its_seat_to_play_on_without_it()
    {
        await Skirmishes.SaveOrdersAsync(gameId, ada, 1, [new RecruitOrder("unit-1", 1)]);

        await Accounts.DeleteAsync(ada);
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);
        await Skirmishes.SetReadyAsync(gameId, morgana, 1, true); // Ada can't ready up any more, so this plays the round

        var view = (await Skirmishes.GetAsync(gameId, merlin))!;
        Assert.Equal(2, view.Planning);
        Assert.Equal(0, view.Game.Seats[2].Units); // her saved orders went with the account
        Assert.Equal(1, view.Game.Seats[2].MissedInARow);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM skirmish_seats WHERE owner_id = $1", ada));
    }

    [Fact]
    public async Task Orders_cannot_change_while_ready()
    {
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);

        Assert.Equal("You're ready: take back Ready to change your orders.", await Skirmishes.SaveOrdersAsync(gameId, merlin, 1, []));
        Assert.Null(await Skirmishes.SetReadyAsync(gameId, merlin, 1, false));
        Assert.Null(await Skirmishes.SaveOrdersAsync(gameId, merlin, 1, [new RecruitOrder("unit-1", 1)]));
    }

    [Fact]
    public async Task A_banned_player_is_not_waited_for()
    {
        await WithDbAsync(db => db.ExecuteAsync("INSERT INTO bans (owner_id, at, reason, banned_by) VALUES ($1, now(), 'test', 'admin')", ada));
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);
        Assert.Equal(2, (await Skirmishes.GetAsync(gameId, merlin))!.Players);

        await Skirmishes.SetReadyAsync(gameId, morgana, 1, true);

        Assert.Equal(2, (await Skirmishes.GetAsync(gameId, merlin))!.Planning);
    }

    [Fact]
    public async Task The_admin_starts_a_game_from_emails()
    {
        var accounts = factory.Services.GetRequiredService<AccountService>();
        await factory.Services.GetRequiredService<RoundKeeper>().TickAsync();
        await factory.Services.GetRequiredService<GameService>().CreateMageAsync(morgana, "Morgana the Grey", MagicSchool.D);

        var (id, number, problem) = await Skirmishes.CreateForEmailsAsync(["merlin@spellbound.test", " MORGANA@spellbound.test ", ""], TimeSpan.FromMinutes(30), accounts);

        Assert.Null(problem);
        Assert.Equal(2, number); // the second Skirmish on this server
        var game = (await Skirmishes.GetAsync(id!.Value, morgana))!.Game;
        Assert.Equal(["merlin", "Morgana the Grey"], game.Seats.Select(s => s.Name));
        Assert.Equal(MagicSchool.D, game.Seats[1].School); // her Realm mage's school
        Assert.Equal(2, game.Rules.Players);
        Assert.Equal("A Skirmish needs 2–6 players: one email per line.", (await Skirmishes.CreateForEmailsAsync(["merlin@spellbound.test"], TimeSpan.FromMinutes(30), accounts)).Problem);
        Assert.Equal("Each player can only take one seat.", (await Skirmishes.CreateForEmailsAsync(["ada@spellbound.test", "Ada@spellbound.test"], TimeSpan.FromMinutes(30), accounts)).Problem);
        Assert.StartsWith("No account has the email nobody@", (await Skirmishes.CreateForEmailsAsync(["ada@spellbound.test", "nobody@spellbound.test"], TimeSpan.FromMinutes(30), accounts)).Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_game_page_and_the_games_list_show_the_skirmish_to_its_players_only()
    {
        using var member = Client();
        using var stranger = Client();
        await SignInAsync(member, "merlin@spellbound.test");
        await SignInAsync(stranger, "stranger@spellbound.test");

        var page = await member.GetStringAsync(new Uri($"/skirmish/{gameId}", UriKind.Relative));
        var games = await member.GetStringAsync(new Uri("/", UriKind.Relative));
        var refused = await stranger.GetStringAsync(new Uri($"/skirmish/{gameId}", UriKind.Relative));

        Assert.Contains("Your orders for round 1", page, StringComparison.Ordinal);
        Assert.Contains("Merlin, School A", page, StringComparison.Ordinal);
        Assert.Contains("Skirmish 1", games, StringComparison.Ordinal);
        Assert.Contains("not in this game.", refused, StringComparison.Ordinal); // the apostrophe is HTML-encoded
    }

    [Fact]
    public async Task The_last_player_not_ready_is_nudged_once()
    {
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);
        Assert.Empty(Notifications()); // two still to go

        await Skirmishes.SetReadyAsync(gameId, morgana, 1, true);
        var nudge = Assert.Single(Notifications());
        Assert.Equal(ada, nudge.OwnerId);
        Assert.Equal("Everyone else is ready in Skirmish 1", nudge.Title);
        Assert.Equal($"/skirmish/{gameId}", nudge.Url);

        await Skirmishes.SetReadyAsync(gameId, morgana, 1, false);
        await Skirmishes.SetReadyAsync(gameId, morgana, 1, true);
        Assert.Empty(Notifications()); // not again this round
    }

    [Fact]
    public async Task When_a_round_plays_everyone_hears_but_the_player_who_played_it()
    {
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);
        await Skirmishes.SetReadyAsync(gameId, morgana, 1, true);
        Notifications(); // the nudge to Ada

        await Skirmishes.SetReadyAsync(gameId, ada, 1, true);

        var played = Notifications();
        Assert.Equal([merlin, morgana], played.Select(n => n.OwnerId));
        Assert.All(played, n => Assert.Equal("Round 1 played in Skirmish 1", n.Title));
    }

    [Fact]
    public async Task A_round_played_by_the_clock_tells_everyone()
    {
        time.Advance(RoundLength);
        await Skirmishes.PlayDueAsync();

        Assert.Equal([merlin, morgana, ada], Notifications().Select(n => n.OwnerId));
    }

    [Fact]
    public async Task Players_not_ready_are_reminded_once_when_time_runs_short()
    {
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);
        Notifications();
        Assert.Equal(0, await Skirmishes.RemindDueAsync()); // an hour-long round: reminders come with 6 minutes left

        time.Advance(RoundLength - TimeSpan.FromMinutes(5));
        Assert.Equal(1, await Skirmishes.RemindDueAsync());
        var reminders = Notifications();
        Assert.Equal([morgana, ada], reminders.Select(n => n.OwnerId));
        Assert.All(reminders, n => Assert.Equal("Time is running out in Skirmish 1", n.Title));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await Skirmishes.RemindDueAsync()); // once a round
        Assert.Empty(Notifications());
    }

    [Fact]
    public async Task Nothing_is_sent_for_a_change_that_was_refused()
    {
        await Skirmishes.SetReadyAsync(gameId, merlin, 1, true);
        await Skirmishes.SetReadyAsync(gameId, morgana, 1, true);
        Notifications();

        Assert.NotNull(await Skirmishes.SetReadyAsync(gameId, ada, 99, true)); // a stale round: refused

        Assert.Empty(Notifications());
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private async Task PlayRoundAsync()
    {
        var planning = (await Skirmishes.GetAsync(gameId, merlin))!.Planning;
        foreach (var owner in new[] { merlin, morgana, ada })
        {
            Assert.Null(await Skirmishes.SetReadyAsync(gameId, owner, planning, true));
        }
    }

    private async Task<int> ClaimableAsync(string ownerId)
    {
        var view = (await Skirmishes.GetAsync(gameId, ownerId))!;
        return view.Game.Claimable(view.Seat).First().Id;
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
                ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(token),
                ["email"] = email,
                ["returnUrl"] = "/",
            }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<string> CreateUserAsync(string email) =>
        (await Accounts.SignInAsync(AccountEndpoints.DevProvider, email, email)).Account!.Id;

    /// <summary>Gives back recorded draws in order, to replay a round.</summary>
    private sealed class Replayed(IReadOnlyList<double> draws) : IRandomSource
    {
        private int next;

        public double NextDouble() => draws[next++];
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
