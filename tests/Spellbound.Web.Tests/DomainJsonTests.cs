using System.Text.Json.Nodes;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>Mages are stored as jsonb snapshots of their private state; nothing may be lost on the way.</summary>
public sealed class DomainJsonTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default with { ProtectionTurns = 0, ProtectionDuration = TimeSpan.Zero, StartingGold = 200_000 };

    [Fact]
    public void A_mage_round_trips_with_its_army_spells_and_losses()
    {
        var mage = BusyMage();

        var json = DomainJson.Serialize(mage);
        var copy = DomainJson.Deserialize<Mage>(json);

        Assert.Equal(json, DomainJson.Serialize(copy));
        Assert.Equal(mage.Army["unit-3"], copy.Army["UNIT-3"]); // the case-insensitive comparer survived
        Assert.Equal(mage.ActiveEffects.Single().RemainingTurns, copy.ActiveEffects.Single().RemainingTurns);
        Assert.Single(copy.RecentLosses);
        Assert.Equal(mage.CreatedAt, copy.CreatedAt);
        Assert.Equal(mage.Networth(Rules), copy.Networth(Rules));
    }

    [Fact]
    public void Rules_round_trip_through_a_game_row_with_the_same_hash()
    {
        var json = DomainJson.Serialize(Rules);
        var stored = JsonNode.Parse(json)!.ToJsonString(); // PostgreSQL reformats jsonb; only the values must survive
        var copy = DomainJson.Deserialize<GameRules>(stored);

        Assert.Equal(Rules, copy);
        Assert.Equal(ActionLog.RulesHash(Rules), ActionLog.RulesHash(copy));
        Assert.Equal(Rules.RoundLength, copy.RoundLength);
    }

    [Fact]
    public void A_field_missing_from_older_json_keeps_its_default()
    {
        var json = JsonNode.Parse(DomainJson.Serialize(BusyMage()))!.AsObject();
        json.Remove("recentLosses");
        json.Remove("army");

        var copy = DomainJson.Deserialize<Mage>(json.ToJsonString());

        Assert.Empty(copy.RecentLosses);
        Assert.Empty(copy.Army);
        copy.Recruit("UNIT-3", 1, T0, Rules); // and the collections still work
        Assert.Equal(1, copy.Army["unit-3"]);
    }

    [Fact]
    public void Records_serialize_as_usual()
    {
        var fame = HallOfFame.Record(Round.First(T0, Rules.RoundLength), [BusyMage()], Rules);

        var copy = DomainJson.Deserialize<HallOfFame>(DomainJson.Serialize(fame));

        Assert.Equal(fame.ByLand.Single(), copy.ByLand.Single());
        Assert.Equal(fame.EndedAt, copy.EndedAt);
    }

    /// <summary>A mage with something in every private collection: an army, a spell running, and a lost battle.</summary>
    private static Mage BusyMage()
    {
        var mage = Mage.Create("owner-1", "Merlin", MagicSchool.Ember, T0, Rules);
        Assert.True(mage.Recruit("UNIT-3", 50, T0, Rules).Succeeded);
        Assert.True(mage.Cast("self-c", T0, Rules).Succeeded);
        var attacker = Mage.Create("owner-2", "Morgana", MagicSchool.Umbra, T0, Rules);
        attacker.Recruit("unit-3", 400, T0, Rules);
        Assert.True(attacker.Attack(mage, T0, Rules, new FixedRandom(0.5)).Report.Succeeded);
        return mage;
    }

    private sealed class FixedRandom(double value) : IRandomSource
    {
        public double NextDouble() => value;
    }
}
