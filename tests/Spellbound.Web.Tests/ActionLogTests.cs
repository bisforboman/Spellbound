using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>The action log: what it records, that it is enough to replay a fight exactly, and who it forgets.</summary>
public sealed class ActionLogTests : TestDatabase, IAsyncLifetime
{
    private const string Merlin = "account-1";
    private const string Morgana = "account-2";

    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, ProtectionDuration = TimeSpan.Zero, StartingGold = 200_000 };

    private readonly WebApplicationFactory<Program> factory;

    public ActionLogTests()
    {
        factory = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<GameRules>();
            services.AddSingleton(Rules);
            services.RemoveAll<IRandomSource>();
            services.AddSingleton<IRandomSource>(new SeededRandom(11));
        }));
    }

    private GameService Game => factory.Services.GetRequiredService<GameService>();

    public async Task InitializeAsync()
    {
        await factory.Services.GetRequiredService<RoundKeeper>().TickAsync();
        await Game.CreateMageAsync(Merlin, "Merlin", MagicSchool.Ember);
        await Game.CreateMageAsync(Morgana, "Morgana", MagicSchool.Dawn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Each_successful_action_is_logged_with_its_inputs_result_and_state_and_a_failure_is_not()
    {
        var report = await Game.ExploreAsync(Merlin, 5);
        var failed = await Game.ExploreAsync(Merlin, 1_000);

        Assert.True(report.Succeeded);
        Assert.False(failed.Succeeded);
        var rows = await RowsAsync(Merlin);
        Assert.Equal(["create", "explore"], rows.Select(r => r.Action));
        var explore = rows[1];
        Assert.Equal(5, explore.Inputs.GetProperty("turns").GetInt32());
        Assert.Equal(report.Lines, explore.Lines);
        Assert.Equal(0, explore.Draws.GetArrayLength()); // no luck involved
        Assert.Equal((await Game.GetMageAsync(Merlin))!.Land, explore.State.GetProperty("land").GetInt32());
        Assert.Equal(ActionLog.RulesHash(Rules), explore.RulesHash);
        Assert.Equal("Merlin", rows[0].Inputs.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_logged_attack_replays_exactly_from_the_mages_before_it()
    {
        await Game.RecruitAsync(Merlin, "unit-3", 400);
        var morganaId = (await Game.GetMageAsync(Morgana))!.Id;
        var attackerBefore = (await WithDbAsync(db => db.LoadMageAsync(1, Merlin)))!; // as stored, not accrued
        var defenderBefore = (await WithDbAsync(db => db.LoadMageAsync(1, Morgana)))!;

        var report = await Game.AttackAsync(Merlin, morganaId);

        Assert.True(report.Succeeded, string.Join(" ", report.Lines));
        var logged = (await RowsAsync(Merlin)).Last();
        Assert.Equal("attack", logged.Action);
        Assert.Equal(Morgana, logged.TargetOwnerId);
        Assert.Equal(morganaId, logged.Inputs.GetProperty("targetMageId").GetGuid());
        var draws = logged.Draws.EnumerateArray().Select(d => d.GetDouble()).ToList();
        Assert.NotEmpty(draws);

        // Replay: the same mages, time, rules and draws give the same battle and the same saved mages.
        var replay = attackerBefore.Attack(defenderBefore, logged.At, Rules, new Replayed(draws));

        Assert.Equal(report.Lines, replay.Report.Lines);
        Assert.Equal(logged.TargetLines, replay.TargetLines);
        Assert.Equal(await StoredJsonAsync(Merlin), DomainJson.Serialize(attackerBefore));
        Assert.Equal(await StoredJsonAsync(Morgana), DomainJson.Serialize(defenderBefore));
    }

    [Fact]
    public async Task Deleting_an_account_deletes_its_actions_and_leaves_others_attacks_on_it_without_its_id()
    {
        await Game.RecruitAsync(Merlin, "unit-3", 400);
        await Game.AttackAsync(Merlin, (await Game.GetMageAsync(Morgana))!.Id);

        Assert.Equal(
            (await Game.GetMageAsync(Morgana))!.Land,
            await WithDbAsync(db => db.ScalarAsync<int>("SELECT (target_state->>'land')::int FROM actions WHERE action = 'attack'")));

        await factory.Services.GetRequiredService<AccountService>().DeleteAsync(Morgana);

        Assert.Empty(await RowsAsync(Morgana));
        var attack = (await RowsAsync(Merlin)).Last();
        Assert.Equal("attack", attack.Action);
        Assert.Null(attack.TargetOwnerId);
    }

    [Fact]
    public async Task The_admin_lookup_shows_a_mages_actions_and_attacks_on_it_newest_first()
    {
        await Game.RecruitAsync(Merlin, "unit-3", 400);
        await Game.AttackAsync(Merlin, (await Game.GetMageAsync(Morgana))!.Id);

        var (mage, actions) = await Game.GetActionLogAsync(" morgana ");

        Assert.Equal("Morgana", mage!.Name);
        Assert.Equal(["attack", "create"], actions.Select(a => a.Action));
        Assert.True(actions[0].Against);
        Assert.Equal(Merlin, actions[0].OtherOwnerId);
        Assert.StartsWith("Merlin (Ember) attacked you", actions[0].Lines[0], StringComparison.Ordinal);
        Assert.Null((await Game.GetActionLogAsync("Nobody")).Mage);
    }

    public override void Dispose()
    {
        factory.Dispose();
        base.Dispose();
    }

    private Task<string> StoredJsonAsync(string ownerId) =>
        WithDbAsync(async db => (await db.ScalarAsync<string>("SELECT data::text FROM mages WHERE game_id = 1 AND owner_id = $1", ownerId))!)
            .ContinueWith(t => DomainJson.Serialize(DomainJson.Deserialize<Mage>(t.Result)), TaskScheduler.Default); // same key order

    private Task<List<Row>> RowsAsync(string ownerId) =>
        WithDbAsync(db => db.QueryAsync(
            """
            SELECT action, at, target_owner_id, inputs::text, draws::text, result::text, state::text, rules_hash
            FROM actions WHERE owner_id = $1 ORDER BY id
            """,
            r =>
            {
                var result = JsonDocument.Parse(r.GetString(5)).RootElement;
                return new Row(
                    r.GetString(0),
                    r.GetFieldValue<DateTimeOffset>(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    JsonDocument.Parse(r.GetString(3)).RootElement,
                    JsonDocument.Parse(r.GetString(4)).RootElement,
                    [.. result.GetProperty("lines").EnumerateArray().Select(l => l.GetString()!)],
                    result.GetProperty("targetLines").ValueKind == JsonValueKind.Array
                        ? [.. result.GetProperty("targetLines").EnumerateArray().Select(l => l.GetString()!)]
                        : [],
                    JsonDocument.Parse(r.GetString(6)).RootElement,
                    r.GetString(7));
            },
            ownerId));

    private sealed record Row(
        string Action, DateTimeOffset At, string? TargetOwnerId, JsonElement Inputs, JsonElement Draws,
        IReadOnlyList<string> Lines, IReadOnlyList<string> TargetLines, JsonElement State, string RulesHash);

    /// <summary>Gives back logged draws in order, as the original luck did.</summary>
    private sealed class Replayed(IReadOnlyList<double> draws) : IRandomSource
    {
        private int next;

        public double NextDouble() => draws[next++];
    }
}
