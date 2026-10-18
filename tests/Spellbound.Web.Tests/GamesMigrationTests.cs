using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>A database from before games existed (one current round, rows keyed by round number) moves over intact.</summary>
public sealed class GamesMigrationTests : TestDatabase
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rounds_become_Realm_games_and_every_row_follows_its_round()
    {
        var old = CreateDatabase(); // empty
        SchemaModule[] before = [Only(HostStore.Schema, 1), Only(new SpellboundSchema().Schema, 2)];
        await SchemaMigrator.MigrateAsync(old, before);
        await WithDbAsync(async db =>
        {
            // Round 2 is being played; round 1 ended with a hall of fame and left its mages behind.
            await db.ExecuteAsync(
                "INSERT INTO current_round (id, number, starts_at, ends_at) VALUES (1, 2, $1, $2)", T0.AddDays(28), T0.AddDays(56));
            await db.ExecuteAsync(
                "INSERT INTO hall_of_fame (round, data) VALUES (1, $1)",
                new Jsonb(DomainJson.Serialize(new HallOfFame(1, T0, T0.AddDays(28), 1, [], []))));
            foreach (var round in new[] { 1, 2 })
            {
                await db.ExecuteAsync(
                    "INSERT INTO mages (round, owner_id, mage_id, data) VALUES ($1, 'account-1', $2, $3)",
                    round, Guid.NewGuid(), new Jsonb(DomainJson.Serialize(Mage.Create("account-1", $"Merlin {round}", MagicSchool.C, T0, GameRules.Default))));
                await db.ExecuteAsync(
                    "INSERT INTO news (round, owner_id, entries, unseen) VALUES ($1, 'account-1', $2, 0)",
                    round, new Jsonb(DomainJson.Serialize(new[] { new NewsEntry(T0, [$"News of round {round}"]) })));
                await db.ExecuteAsync(
                    """
                    INSERT INTO actions (round, owner_id, at, action, inputs, draws, result, state, rules_hash)
                    VALUES ($1, 'account-1', $2, 'explore', '{}', '[]', '{}', '{}', $3)
                    """,
                    round, T0, $"hash-{round}");
            }

            return 0;
        }, old);

        await SchemaMigrator.MigrateAsync(old, [HostStore.Schema, new SpellboundSchema().Schema]);

        await WithDbAsync(async db =>
        {
            var running = (await db.LoadRunningGameAsync(SpellboundHost.RealmMode))!;
            Assert.Equal(2, running.Id);
            Assert.Equal(new Round(2, T0.AddDays(28), T0.AddDays(56)), running.Round);
            Assert.Equal("Merlin 2", (await db.LoadMageAsync(running.Id, "account-1"))!.Name);
            Assert.Equal("News of round 2", (await db.LoadNewsAsync(running.Id, "account-1")).Entries.Single().Lines[0]);
            Assert.Equal("hash-2", await db.ScalarAsync<string>("SELECT rules_hash FROM actions WHERE game_id = 2"));
            var configured = GameRules.Default with { MaxTurns = 99 };
            Assert.Same(configured, running.RulesOf(configured)); // it ran under the configured rules, so it keeps them

            var ended = (await db.LoadGameAsync(1))!;
            Assert.Equal(new Round(1, T0, T0.AddDays(28)), ended.Round); // times from its hall of fame
            Assert.Equal("Merlin 1", (await db.LoadMageAsync(1, "account-1"))!.Name);

            // New games are numbered and keyed after the old ones.
            var next = await db.ScalarAsync<long>(
                "INSERT INTO games (mode, number, starts_at, ends_at, rules, created_at) VALUES ('test', 1, now(), now(), '{}', now()) RETURNING id");
            Assert.Equal(3, next);
            return 0;
        }, old);
    }

    private static SchemaModule Only(SchemaModule module, int upTo) => module with { Scripts = [.. module.Scripts.Where(s => s.Version <= upTo)] };
}
