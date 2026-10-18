namespace Spellbound.Domain;

/// <summary>
/// The five schools of magic. They form a ring: A → B → C → D → E → A. The members are stored (in every mage, hall of
/// fame and logged action), so they stay neutral; players see <see cref="MagicSchoolExtensions.DisplayName"/>.
/// </summary>
public enum MagicSchool
{
    A,
    B,
    C,
    D,
    E,
}

public static class MagicSchoolExtensions
{
    /// <summary>
    /// The name players see. A placeholder until the schools are named (docs/game-design.md, "Names to decide"); naming
    /// them changes only this, not stored data.
    /// </summary>
    public static string DisplayName(this MagicSchool school) => $"School {school}";

    private static readonly int SchoolCount = Enum.GetValues<MagicSchool>().Length;

    /// <summary>The school this one has the advantage over (the next one in the ring).</summary>
    public static MagicSchool StrongAgainst(this MagicSchool school) =>
        (MagicSchool)(((int)school + 1) % SchoolCount);

    /// <summary>The school that has the advantage over this one (the previous one in the ring).</summary>
    public static MagicSchool WeakAgainst(this MagicSchool school) =>
        (MagicSchool)(((int)school + SchoolCount - 1) % SchoolCount);
}
