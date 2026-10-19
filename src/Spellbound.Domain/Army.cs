namespace Spellbound.Domain;

/// <summary>The kinds of unit (docs/game-design.md, Units).</summary>
public enum UnitKind
{
    /// <summary>The basic unit: cheap, weak, no upkeep.</summary>
    Imp,

    /// <summary>The strong unit: costs more and needs upkeep every round.</summary>
    Golem,
}

/// <summary>A number of units of each kind: those on a tile, or those sent somewhere.</summary>
public readonly record struct Army(int Imps, int Golems)
{
    public static Army None => default;

    public static Army Of(UnitKind kind, int count) => kind == UnitKind.Golem ? new(0, count) : new(count, 0);

    public bool IsEmpty => Imps == 0 && Golems == 0;

    public bool IsValid => Imps >= 0 && Golems >= 0;

    /// <summary>True when every unit in <paramref name="part"/> can come out of this army.</summary>
    public bool Holds(Army part) => part.Imps <= Imps && part.Golems <= Golems;

    public int Power(GameRules rules) => Imps * rules.ImpPower + Golems * rules.GolemPower;

    public static Army operator +(Army a, Army b) => new(a.Imps + b.Imps, a.Golems + b.Golems);

    public static Army operator -(Army a, Army b) => new(a.Imps - b.Imps, a.Golems - b.Golems);

    public override string ToString() => (Imps, Golems) switch
    {
        (0, 0) => "no units",
        (_, 0) => Count(Imps, "Imp"),
        (0, _) => Count(Golems, "Golem"),
        _ => $"{Count(Imps, "Imp")} and {Count(Golems, "Golem")}",
    };

    private static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
