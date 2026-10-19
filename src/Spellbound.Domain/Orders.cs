namespace Spellbound.Domain;

/// <summary>The one decision a player makes each round (docs/game-design.md, Rounds and stances).</summary>
public enum Stance
{
    /// <summary>Spend mana to claim one free tile next to your land.</summary>
    Attune,

    /// <summary>Spend mana to summon units of one kind on one of your tiles.</summary>
    Summon,
}

/// <summary>Units moving one step, from one of a player's tiles to a neighbouring one of theirs.</summary>
public sealed record Move(Hex From, Hex To, Army Units);

/// <summary>
/// A seat's secret commitment for a round: one stance, what it aims at, and any moves. A stance with no target does
/// nothing (a player can always hold back). For Summon, <see cref="Kind"/> and <see cref="Count"/> say what to summon.
/// </summary>
public sealed record Orders(int Seat, Stance Stance, Hex? Target, IReadOnlyList<Move> Moves, UnitKind Kind = UnitKind.Imp, int Count = 0)
{
    public static Orders Attune(int seat, Hex tile, params IReadOnlyList<Move> moves) => new(seat, Stance.Attune, tile, moves);

    public static Orders Summon(int seat, Hex tile, UnitKind kind, int count, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Summon, tile, moves, kind, count);

    /// <summary>Picks a stance but aims it at nothing: only the moves happen.</summary>
    public static Orders Hold(int seat, params IReadOnlyList<Move> moves) => new(seat, Stance.Summon, null, moves);
}
