using Microsoft.Extensions.Configuration;
using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

public class RulesSetupTests
{
    private static GameRules Bind(params (string Key, string Value)[] settings) =>
        RulesSetup.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Fact]
    public void Without_settings_the_rules_are_the_defaults()
    {
        Assert.Equal(MatchJson.Serialize(GameRules.Default), MatchJson.Serialize(Bind()));
    }

    [Fact]
    public void A_setting_overrides_one_number_and_keeps_the_rest()
    {
        var rules = Bind(("GameRules:StartingMana", "12"), ("GameRules:Rounds", "6"));

        Assert.Equal(12, rules.StartingMana);
        Assert.Equal(6, rules.Rounds);
        Assert.Equal(GameRules.Default.GolemCost, rules.GolemCost);
        Assert.Equal(GameRules.Default.WildYields, rules.WildYields);
    }

    [Fact]
    public void A_list_in_the_settings_replaces_the_default_list()
    {
        var rules = Bind(("GameRules:WildYields:0", "0"), ("GameRules:WildYields:1", "4"), ("GameRules:LaterUnveilRounds:0", "3"));

        Assert.Equal([0, 4], rules.WildYields);
        Assert.Equal([3], rules.LaterUnveilRounds);
        Assert.Equal(GameRules.Default.SteadyYields, rules.SteadyYields);
    }
}
