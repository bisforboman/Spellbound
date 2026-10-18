namespace Spellbound.Domain;

/// <summary>What a self-spell modifies while it is active.</summary>
public enum EffectKind
{
    GoldIncome,
    ManaIncome,
    ExploreYield,
    BuildCost,
    MeditateYield,

    /// <summary>Scales the success chance of hexes cast at you.</summary>
    HexWard,
}

/// <summary>
/// A self-spell definition. Durations are measured in the caster's own spent turns.
/// A spell without a school is known by every mage.
/// </summary>
public sealed record SpellDefinition(
    string Id,
    string Name,
    MagicSchool? School,
    int ManaCost,
    int TurnCost,
    int DurationTurns,
    EffectKind Effect,
    double Multiplier,
    string Description);

/// <summary>What a hex does to its target.</summary>
public enum HexEffect
{
    /// <summary>Kills a share of every unit type.</summary>
    KillUnits,

    /// <summary>Destroys a share of the target's gold.</summary>
    DestroyGold,

    /// <summary>Takes a share of the target's mana; the caster keeps what fits in storage.</summary>
    StealMana,

    /// <summary>Burns a share of the target's buildings, leaving free land.</summary>
    BurnBuildings,

    /// <summary>Takes a fixed number of turns from the target's turns on hand.</summary>
    DrainTurns,
}

/// <summary>An offensive spell cast at another mage. Strength is a share (0–1) or, for DrainTurns, a count.</summary>
public sealed record HexDefinition(
    string Id,
    string Name,
    MagicSchool School,
    int ManaCost,
    int TurnCost,
    HexEffect Effect,
    double Strength,
    string Description);

/// <summary>
/// All spells in the game: one self-buff per school, a ward every mage knows, and one hex per school.
/// All are known from the start.
/// </summary>
// Names are placeholders until they're decided (docs/game-design.md, "Names to decide"); ids are stored, so they're neutral.
public static class SpellBook
{
    public static IReadOnlyList<SpellDefinition> All { get; } =
    [
        new("self-a", "Dawn self-spell", MagicSchool.Dawn, 1_500, 2, 30, EffectKind.GoldIncome, 1.25,
            "Gold income +25%."),
        new("self-b", "Grove self-spell", MagicSchool.Grove, 1_500, 2, 30, EffectKind.ExploreYield, 1.5,
            "Exploring yields +50% land."),
        new("self-c", "Ember self-spell", MagicSchool.Ember, 1_200, 2, 30, EffectKind.ManaIncome, 1.3,
            "Node mana income +30%."),
        new("self-d", "Umbra self-spell", MagicSchool.Umbra, 1_500, 2, 30, EffectKind.BuildCost, 0.8,
            "Buildings cost 20% less gold."),
        new("self-e", "Veil self-spell", MagicSchool.Veil, 1_200, 2, 30, EffectKind.MeditateYield, 1.5,
            "Meditation gives +50% mana."),
        new("ward", "Ward spell", null, 1_000, 2, 30, EffectKind.HexWard, 0.5,
            "Hexes cast at you succeed half as often."),
    ];

    public static IReadOnlyList<HexDefinition> Hexes { get; } =
    [
        new("hex-a", "Dawn hex", MagicSchool.Dawn, 2_000, 2, HexEffect.KillUnits, 0.05,
            "5% of their units die."),
        new("hex-b", "Grove hex", MagicSchool.Grove, 2_000, 2, HexEffect.DestroyGold, 0.08,
            "8% of their gold is destroyed."),
        new("hex-c", "Ember hex", MagicSchool.Ember, 2_000, 2, HexEffect.StealMana, 0.10,
            "Take 10% of their mana."),
        new("hex-d", "Umbra hex", MagicSchool.Umbra, 2_000, 2, HexEffect.BurnBuildings, 0.03,
            "3% of their buildings are destroyed."),
        new("hex-e", "Veil hex", MagicSchool.Veil, 2_500, 2, HexEffect.DrainTurns, 3,
            "They lose 3 turns."),
    ];

    public static SpellDefinition? Find(string spellId) =>
        All.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<SpellDefinition> ForSchool(MagicSchool school) =>
        All.Where(s => s.School is null || s.School == school);

    public static HexDefinition? FindHex(string hexId) =>
        Hexes.FirstOrDefault(h => string.Equals(h.Id, hexId, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<HexDefinition> HexesForSchool(MagicSchool school) =>
        Hexes.Where(h => h.School == school);
}

/// <summary>A spell currently affecting a mage.</summary>
public sealed class ActiveEffect
{
    private ActiveEffect()
    {
        SpellId = string.Empty; // for deserialization
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
