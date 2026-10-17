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
/// All spells in the game: one self-buff per school, a Barrier every mage knows, and one hex per school.
/// All are known from the start.
/// </summary>
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
        new("barrier", "Arcane Barrier", null, 1_000, 2, 30, EffectKind.HexWard, 0.5,
            "A shimmering ward: hexes cast at you succeed half as often."),
    ];

    public static IReadOnlyList<HexDefinition> Hexes { get; } =
    [
        new("smite", "Smite", MagicSchool.Radiant, 2_000, 2, HexEffect.KillUnits, 0.05,
            "Holy fire falls on their army: 5% of their units die."),
        new("locust-swarm", "Locust Swarm", MagicSchool.Verdant, 2_000, 2, HexEffect.DestroyGold, 0.08,
            "Locusts devour their stores: 8% of their gold is lost."),
        new("mana-drain", "Mana Drain", MagicSchool.Tidal, 2_000, 2, HexEffect.StealMana, 0.10,
            "Siphon their pools: take 10% of their mana."),
        new("firestorm", "Firestorm", MagicSchool.Ember, 2_000, 2, HexEffect.BurnBuildings, 0.03,
            "Rain fire on their lands: 3% of their buildings burn."),
        new("nightmare", "Nightmare", MagicSchool.Umbral, 2_500, 2, HexEffect.DrainTurns, 3,
            "Haunt their sleep: they lose 3 turns."),
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
