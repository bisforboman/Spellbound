using System.Text.Json.Nodes;
using Spellbound.Domain;
using Spellbound.Domain.Skirmish;
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
    public void A_skirmish_game_in_progress_round_trips()
    {
        var game = SkirmishGame.Start(
            [("Merlin", MagicSchool.A), ("Morgana", MagicSchool.B), ("Ada", MagicSchool.C)], SkirmishRules.Default, new SeededRandom(3));
        var home = game.Board.Homes[0];
        var claim = game.Claimable(0).First().Id;
        game.PlayRound(
            new Dictionary<int, IReadOnlyList<SkirmishOrder>>
            {
                [0] = [new ClaimOrder(claim), new BuildOrder(home, BuildingType.Town, 2), new RecruitOrder("unit-3", 2)],
            },
            new SeededRandom(4));

        var json = DomainJson.Serialize(game);
        var copy = DomainJson.Deserialize<SkirmishGame>(JsonNode.Parse(json)!.ToJsonString());

        Assert.Equal(json, DomainJson.Serialize(copy));
        Assert.Equal(1, copy.Round);
        Assert.Equal(game.Rules, copy.Rules);
        Assert.Equal(0, copy.Board[claim].Owner);
        Assert.Equal(game.Board[claim].Neighbours, copy.Board[claim].Neighbours);
        Assert.Equal(game.Deck, copy.Deck);
        Assert.Equal(2, copy.Seats[0].Army["UNIT-3"]); // the case-insensitive comparer survived
        Assert.Equal(game.Score(0), copy.Score(0));

        // And it plays on exactly as the original does.
        var next = new Dictionary<int, IReadOnlyList<SkirmishOrder>> { [1] = [new ClaimOrder(game.Claimable(1).First().Id)] };
        Assert.Equal(game.PlayRound(next, new SeededRandom(5)).Public, copy.PlayRound(next, new SeededRandom(5)).Public);
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
        var mage = Mage.Create("owner-1", "Merlin", MagicSchool.C, T0, Rules);
        Assert.True(mage.Recruit("UNIT-3", 50, T0, Rules).Succeeded);
        Assert.True(mage.Cast("self-c", T0, Rules).Succeeded);
        var attacker = Mage.Create("owner-2", "Morgana", MagicSchool.D, T0, Rules);
        attacker.Recruit("unit-3", 400, T0, Rules);
        Assert.True(attacker.Attack(mage, T0, Rules, new FixedRandom(0.5)).Report.Succeeded);
        return mage;
    }

    private sealed class FixedRandom(double value) : IRandomSource
    {
        public double NextDouble() => value;
    }
}
