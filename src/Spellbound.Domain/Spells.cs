namespace Spellbound.Domain;

/// <summary>What a self-spell modifies while it is active.</summary>
public enum EffectKind
{
    GoldIncome,
    ManaIncome,
    ExploreYield,
    BuildCost,
    MeditateYield,
}

/// <summary>A spell definition. Durations are measured in the caster's own spent turns.</summary>
public sealed record SpellDefinition(
    string Id,
    string Name,
    MagicSchool School,
    int ManaCost,
    int TurnCost,
    int DurationTurns,
    EffectKind Effect,
    double Multiplier,
    string Description);

/// <summary>All spells in the game. Milestone 1: one self-buff per school, known from the start.</summary>
public static class SpellBook
{
    public static IReadOnlyList<SpellDefinition> All { get; } =
    [
        new("blessed-harvest", "Blessed Harvest", MagicSchool.Radiant, 1_500, 2, 30, EffectKind.GoldIncome, 1.25,
            "Your lands prosper: gold income +25%."),
        new("overgrowth", "Overgrowth", MagicSchool.Verdant, 1_500, 2, 30, EffectKind.ExploreYield, 1.5,
            "The wilds open up to you: exploring yields +50% land."),
        new("wellspring", "Wellspring", MagicSchool.Tidal, 1_200, 2, 30, EffectKind.ManaIncome, 1.3,
            "Mana flows freely: node income +30%."),
        new("forge-fire", "Forge Fire", MagicSchool.Ember, 1_500, 2, 30, EffectKind.BuildCost, 0.8,
            "Your builders work by magic flame: buildings cost 20% less gold."),
        new("night-whispers", "Night Whispers", MagicSchool.Umbral, 1_200, 2, 30, EffectKind.MeditateYield, 1.5,
            "Voices in the dark guide your meditation: +50% meditation mana."),
    ];

    public static SpellDefinition? Find(string spellId) =>
        All.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<SpellDefinition> ForSchool(MagicSchool school) =>
        All.Where(s => s.School == school);
}

/// <summary>A spell currently affecting a mage.</summary>
public sealed class ActiveEffect
{
    private ActiveEffect()
    {
        SpellId = string.Empty; // EF Core
    }

    public ActiveEffect(string spellId, int remainingTurns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spellId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(remainingTurns);
        SpellId = spellId;
        RemainingTurns = remainingTurns;
    }

    public string SpellId { get; private set; }

    public int RemainingTurns { get; internal set; }
}
