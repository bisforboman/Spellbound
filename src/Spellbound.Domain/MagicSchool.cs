namespace Spellbound.Domain;

/// <summary>The five schools of magic. They form a ring: Radiant → Verdant → Tidal → Ember → Umbral → Radiant.</summary>
public enum MagicSchool
{
    Radiant,
    Verdant,
    Tidal,
    Ember,
    Umbral,
}

public static class MagicSchoolExtensions
{
    private static readonly int SchoolCount = Enum.GetValues<MagicSchool>().Length;

    /// <summary>The school this one has the advantage over (the next one in the ring).</summary>
    public static MagicSchool StrongAgainst(this MagicSchool school) =>
        (MagicSchool)(((int)school + 1) % SchoolCount);

    /// <summary>The school that has the advantage over this one (the previous one in the ring).</summary>
    public static MagicSchool WeakAgainst(this MagicSchool school) =>
        (MagicSchool)(((int)school + SchoolCount - 1) % SchoolCount);
}
