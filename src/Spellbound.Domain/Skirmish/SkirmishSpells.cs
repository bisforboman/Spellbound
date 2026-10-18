using System.Globalization;

namespace Spellbound.Domain.Skirmish;

/// <summary>
/// What the Realm's spells and hexes (<see cref="SpellBook"/>) do in Skirmish, which has rounds and regions instead of
/// turns and acres. Each keeps its school and its effect, with a Skirmish meaning; prices and strengths are in
/// <see cref="SkirmishRules"/>.
/// </summary>
public static class SkirmishSpells
{
    public static int ManaCost(SpellDefinition spell, SkirmishRules rules) => spell.School is null ? rules.WardMana : rules.SelfSpellMana;

    /// <summary>What the self-spell does in Skirmish, for the page and the report.</summary>
    public static string Describe(SpellDefinition spell, SkirmishRules rules) => spell.Effect switch
    {
        EffectKind.GoldIncome => $"Gold income {Times(spell.Multiplier)} for {rules.SpellRounds} rounds.",
        EffectKind.ManaIncome => $"Mana income {Times(spell.Multiplier)} for {rules.SpellRounds} rounds.",
        EffectKind.BuildCost => $"Buildings cost {Times(spell.Multiplier)} gold for {rules.SpellRounds} rounds.",
        EffectKind.ExploreYield => $"Your weight in contested claims {Times(spell.Multiplier)} for {rules.SpellRounds} rounds.",
        EffectKind.MeditateYield => "One more action next round.",
        EffectKind.HexWard => $"Hexes cast at you succeed {Times(spell.Multiplier)} as often for {rules.SpellRounds} rounds.",
        _ => spell.Description,
    };

    /// <summary>What the hex does in Skirmish, for the page and the report.</summary>
    public static string Describe(HexDefinition hex, SkirmishRules rules) => hex.Effect switch
    {
        HexEffect.KillUnits => $"{rules.HexKillPercent}% of their units die.",
        HexEffect.DestroyGold => $"{rules.HexGoldPercent}% of their gold is destroyed.",
        HexEffect.StealMana => $"Take {rules.HexManaPercent}% of their mana.",
        HexEffect.BurnBuildings => $"{rules.HexBuildingsDestroyed} building{(rules.HexBuildingsDestroyed == 1 ? "" : "s")} in their most built region is destroyed.",
        HexEffect.DrainTurns => $"They have {rules.HexActionsTaken} action{(rules.HexActionsTaken == 1 ? "" : "s")} fewer next round.",
        _ => hex.Description,
    };

    private static string Times(double multiplier) => "×" + multiplier.ToString("0.##", CultureInfo.InvariantCulture);
}
