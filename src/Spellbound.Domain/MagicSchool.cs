namespace Spellbound.Domain;

/// <summary>
/// The five schools of magic (docs/mageborn-design.md, "Five schools of magic"), in wheel order: Dawn – Grove – Ember –
/// Umbra – Veil – back to Dawn. The members are stored (in every mage, hall of fame and logged action), so renaming one
/// changes saved data.
/// </summary>
public enum MagicSchool
{
    Dawn,
    Grove,
    Ember,
    Umbra,
    Veil,
}

public static class MagicSchoolExtensions
{
    /// <summary>The schools in wheel order; each school's neighbours are the ones beside it, wrapping around.</summary>
    public static IReadOnlyList<MagicSchool> Wheel { get; } = Enum.GetValues<MagicSchool>();

    /// <summary>The name players see.</summary>
    public static string DisplayName(this MagicSchool school) => school.ToString();

    /// <summary>The two schools beside this one on the wheel: they lend it magic cheaply.</summary>
    public static IReadOnlyList<MagicSchool> Neighbours(this MagicSchool school) =>
        [Step(school, -1), Step(school, 1)];

    /// <summary>The two schools this one doesn't touch on the wheel: their magic costs it double.</summary>
    public static IReadOnlyList<MagicSchool> Opposites(this MagicSchool school) =>
        [Step(school, 2), Step(school, -2)];

    // Today's counter ring (until the battle rework replaces it): each school has the advantage over the next one on
    // the wheel.

    /// <summary>The school this one has the advantage over (the next one on the wheel).</summary>
    public static MagicSchool StrongAgainst(this MagicSchool school) => Step(school, 1);

    /// <summary>The school that has the advantage over this one (the previous one on the wheel).</summary>
    public static MagicSchool WeakAgainst(this MagicSchool school) => Step(school, -1);

    private static MagicSchool Step(MagicSchool school, int steps) =>
        Wheel[(((int)school + steps) % Wheel.Count + Wheel.Count) % Wheel.Count];
}
