using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Spellbound.Domain;
using Spellbound.Domain.Skirmish;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Players setting up their own Skirmishes and joining them with an invite code, through the real app.</summary>
public sealed class SkirmishLobbyTests : TestDatabase, IAsyncLifetime
{
    private static readonly TimeSpan RoundLength = TimeSpan.FromHours(12);
    private static readonly SkirmishRules ThreePlayers = SkirmishRules.Default with { Players = 3 };

    private readonly WebApplicationFactory<Program> factory;
    private string merlin = string.Empty;
    private string morgana = string.Empty;
    private string ada = string.Empty;

    public SkirmishLobbyTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
            // Keep notifications in the queue, where the tests read them, instead of sending them.
            services.Remove(services.Single(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && d.ImplementationType == typeof(PushSender)))));
    }

    private SkirmishLobbyService Lobbies => factory.Services.GetRequiredService<SkirmishLobbyService>();

    private SkirmishService Skirmishes => factory.Services.GetRequiredService<SkirmishService>();

    private AccountService Accounts => factory.Services.GetRequiredService<AccountService>();

    public async Task InitializeAsync()
    {
        merlin = await CreateUserAsync("merlin@spellbound.test");
        morgana = await CreateUserAsync("morgana@spellbound.test");
        ada = await CreateUserAsync("ada@spellbound.test");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_game_waits_for_players_and_starts_when_the_last_one_joins()
    {
        var code = await CreateAsync(merlin, "Merlin", ThreePlayers);

        Assert.Null(await Lobbies.JoinAsync(code.ToLowerInvariant(), morgana, "Morgana", MagicSchool.B)); // typed in any case
        var waiting = (await Lobbies.FindAsync(code))!;
        Assert.False(waiting.Started);
        Assert.Equal(["Merlin", "Morgana"], waiting.Players.Select(p => p.Name));
        Assert.Single(await Lobbies.GetMineAsync(morgana));
        Assert.Empty(Notifications());

        Assert.Null(await Lobbies.JoinAsync(code, ada, "Ada", MagicSchool.C));

        var started = (await Lobbies.FindAsync(code))!;
        Assert.True(started.Started);
        Assert.Empty(started.Players); // the seats moved into the game
        Assert.Empty(await Lobbies.GetMineAsync(morgana));
        var view = (await Skirmishes.GetAsync(started.GameId!.Value, ada))!;
        Assert.Equal(["Merlin", "Morgana", "Ada"], view.Game.Seats.Select(s => s.Name)); // in the order they joined
        Assert.Equal(MagicSchool.C, view.Game.Seats[2].School);
        Assert.Equal(RoundLength, view.RoundLength);
        Assert.Equal(3, view.Game.Rules.Players);
        // Everyone waiting hears it started, but not Ada, whose join started it.
        Assert.Equal(new[] { merlin, morgana }.Order(), Notifications().Select(n => n.OwnerId).Order());
    }

    [Fact]
    public async Task The_creator_can_start_early_with_whoever_has_joined()
    {
        var code = await CreateAsync(merlin, "Merlin", SkirmishRules.Default with { Players = 6, Rounds = 6, Attacks = false });
        Assert.Equal("A Skirmish needs at least 2 players.", await Lobbies.StartNowAsync(code, merlin));
        Assert.Null(await Lobbies.JoinAsync(code, morgana, "Morgana", MagicSchool.B));

        Assert.Equal("Only the player who set up the game can start it early.", await Lobbies.StartNowAsync(code, morgana));
        Assert.Null(await Lobbies.StartNowAsync(code, merlin));

        var view = (await Skirmishes.GetAsync((await Lobbies.FindAsync(code))!.GameId!.Value, morgana))!;
        Assert.Equal(2, view.Game.Rules.Players);
        Assert.Equal(6, view.Game.Rules.Rounds);
        Assert.False(view.Game.Rules.Attacks);
        Assert.Equal("That game has already started.", await Lobbies.JoinAsync(code, ada, "Ada", MagicSchool.C));
    }

    [Fact]
    public async Task Joining_is_refused_without_changing_anything()
    {
        var code = await CreateAsync(merlin, "Merlin", SkirmishRules.Default with { Players = 2 });

        Assert.Equal("No game has that code.", await Lobbies.JoinAsync("NOPE99", morgana, "Morgana", MagicSchool.B));
        Assert.Equal("You've already joined this game.", await Lobbies.JoinAsync(code, merlin, "Merlin", MagicSchool.A));
        Assert.Equal("Someone in this game is already called merlin.", await Lobbies.JoinAsync(code, morgana, "merlin", MagicSchool.B));
        Assert.Equal($"Name must be {Mage.MinNameLength}–{Mage.MaxNameLength} characters.", await Lobbies.JoinAsync(code, morgana, "Mo", MagicSchool.B));
        await WithDbAsync(db => db.ExecuteAsync(
            "INSERT INTO bans (owner_id, at, reason, banned_by) VALUES ($1, now(), 'test', 'admin')", ada));
        Assert.Equal("Your account is banned.", await Lobbies.JoinAsync(code, ada, "Ada", MagicSchool.C));

        Assert.Equal(["Merlin"], (await Lobbies.FindAsync(code))!.Players.Select(p => p.Name));
    }

    [Fact]
    public async Task Setting_up_is_refused_outside_the_limits()
    {
        Assert.Equal("A Skirmish has 2–6 players.", (await Lobbies.CreateAsync(merlin, "Merlin", MagicSchool.A, SkirmishRules.Default with { Players = 7 }, RoundLength, false)).Problem);
        Assert.Equal("A Skirmish lasts 4–24 rounds.", (await Lobbies.CreateAsync(merlin, "Merlin", MagicSchool.A, SkirmishRules.Default with { Rounds = 100 }, RoundLength, false)).Problem);
        Assert.Equal("A round lasts between a minute and a week.", (await Lobbies.CreateAsync(merlin, "Merlin", MagicSchool.A, SkirmishRules.Default, TimeSpan.FromSeconds(5), false)).Problem);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM skirmish_lobbies"));
    }

    [Fact]
    public async Task A_player_can_be_in_only_so_many_games_at_once()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync(merlin, "Merlin", ThreePlayers);
        }

        var (code, problem) = await Lobbies.CreateAsync(merlin, "Merlin", MagicSchool.A, ThreePlayers, RoundLength, false);

        Assert.Null(code);
        Assert.Equal("You're in 3 Skirmishes already, the most at once. Finish or leave one first.", problem);
        var other = await CreateAsync(morgana, "Morgana", ThreePlayers);
        Assert.Equal(problem, await Lobbies.JoinAsync(other, merlin, "Merlin", MagicSchool.A));
    }

    [Fact]
    public async Task Two_players_taking_the_last_seat_at_once_start_one_game()
    {
        var code = await CreateAsync(merlin, "Merlin", SkirmishRules.Default with { Players = 2 });

        var results = await Task.WhenAll(
            Lobbies.JoinAsync(code, morgana, "Morgana", MagicSchool.B),
            Lobbies.JoinAsync(code, ada, "Ada", MagicSchool.C));

        Assert.Single(results, r => r is null);
        Assert.Single(results, r => r is "That game has already started.");
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM skirmish_games"));
        Assert.Equal(2, await CountAsync("SELECT count(*) FROM skirmish_seats"));
    }

    [Fact]
    public async Task Leaving_frees_the_seat_and_the_creator_leaving_calls_the_game_off()
    {
        var code = await CreateAsync(merlin, "Merlin", ThreePlayers);
        await Lobbies.JoinAsync(code, morgana, "Morgana", MagicSchool.B);
        await Lobbies.JoinAsync(code, ada, "Ada", MagicSchool.C) /* fills it: starts */;
        var second = await CreateAsync(merlin, "Merlin", ThreePlayers);
        await Lobbies.JoinAsync(second, morgana, "Morgana", MagicSchool.B);
        Notifications();

        Assert.Null(await Lobbies.LeaveAsync(second, morgana));
        Assert.Equal(["Merlin"], (await Lobbies.FindAsync(second))!.Players.Select(p => p.Name));
        Assert.Equal("That game has already started.", await Lobbies.LeaveAsync(code, morgana));

        await Lobbies.JoinAsync(second, morgana, "Morgana", MagicSchool.B);
        Assert.Null(await Lobbies.LeaveAsync(second, merlin));

        Assert.Null(await Lobbies.FindAsync(second));
        Assert.Equal(morgana, Assert.Single(Notifications()).OwnerId); // told it's called off
    }

    [Fact]
    public async Task Public_games_are_listed_and_private_ones_are_not()
    {
        await CreateAsync(merlin, "Merlin", ThreePlayers);
        var (open, _) = await Lobbies.CreateAsync(morgana, "Morgana", MagicSchool.B, ThreePlayers, RoundLength, isPublic: true);

        Assert.Equal(open, Assert.Single(await Lobbies.ListOpenAsync()).Code);
    }

    [Fact]
    public async Task Deleting_an_account_calls_off_its_games_and_leaves_the_others()
    {
        var mine = await CreateAsync(merlin, "Merlin", ThreePlayers);
        var theirs = await CreateAsync(morgana, "Morgana", ThreePlayers);
        await Lobbies.JoinAsync(theirs, merlin, "Merlin", MagicSchool.A);
        var started = await CreateAsync(merlin, "Merlin", SkirmishRules.Default with { Players = 2 });
        await Lobbies.JoinAsync(started, ada, "Ada", MagicSchool.C);

        await Accounts.DeleteAsync(merlin);

        Assert.Null(await Lobbies.FindAsync(mine));
        Assert.Equal(["Morgana"], (await Lobbies.FindAsync(theirs))!.Players.Select(p => p.Name));
        Assert.Null((await Lobbies.FindAsync(started))!.CreatorId);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM skirmish_lobby_players WHERE owner_id = $1", merlin));
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private async Task<string> CreateAsync(string ownerId, string name, SkirmishRules rules)
    {
        var (code, problem) = await Lobbies.CreateAsync(ownerId, name, MagicSchool.A, rules, RoundLength, isPublic: false);
        Assert.Null(problem);
        Assert.Matches("^[A-Z2-9]{6}$", code);
        return code!;
    }

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

    private async Task<string> CreateUserAsync(string email) =>
        (await Accounts.SignInAsync(AccountEndpoints.DevProvider, email, email)).Account!.Id;
}
