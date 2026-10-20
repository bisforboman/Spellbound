namespace Spellbound.Domain;

/// <summary>The one decision a player makes each round (docs/game-design.md, Rounds and stances).</summary>
public enum Stance
{
    /// <summary>Send units against a rival's tile or a monster-guarded tile next to your land.</summary>
    Attack,

    /// <summary>Spend mana to claim one free tile next to your land.</summary>
    Attune,

    /// <summary>Spend mana to summon units of one kind on one of your tiles.</summary>
    Summon,

    /// <summary>Spend mana on one spell (docs/game-design.md, Spells).</summary>
    Cast,
}

/// <summary>The spells (docs/game-design.md, Spells). Each lasts this round, except Lure, which weakens monsters for
/// good.</summary>
public enum Spell
{
    /// <summary>Burn units' power on a rival's tile next to yours, weakest first.</summary>
    Scorch,

    /// <summary>One of your tiles defends with more power this round.</summary>
    Ward,

    /// <summary>Move units between two of your tiles, any distance apart.</summary>
    Leyline,

    /// <summary>A rival's tile next to yours yields nothing in the next round's income.</summary>
    Blight,

    /// <summary>A monster guard next to your land loses power for good.</summary>
    Lure,
}

/// <summary>Units moving one step, from one of a player's tiles to a neighbouring one of theirs.</summary>
public sealed record Move(Hex From, Hex To, Army Units);

/// <summary>
/// A seat's secret commitment for a round: one stance, what it aims at, and any moves. A stance with no target does
/// nothing (a player can always hold back). For Summon, <see cref="Kind"/> and <see cref="Count"/> say what to summon;
/// for Attack, <see cref="Attackers"/> says which units go, from which of the seat's tiles next to the target; for Cast,
/// <see cref="Spell"/> says which spell, <see cref="Count"/> how much power Scorch burns, and <see cref="Transfer"/>
/// what Leyline moves (its <see cref="Target"/> is where the units leave from).
/// </summary>
public sealed record Orders(int Seat, Stance Stance, Hex? Target, IReadOnlyList<Move> Moves, UnitKind Kind = UnitKind.Imp, int Count = 0)
{
    public IReadOnlyList<Move> Attackers { get; init; } = [];

    public Spell Spell { get; init; }

    public Move? Transfer { get; init; }

    public static Orders Cast(int seat, Spell spell, Hex tile, int count = 0, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Cast, tile, moves, Count: count) { Spell = spell };

    public static Orders CastLeyline(int seat, Move transfer, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Cast, transfer.From, moves) { Spell = Spell.Leyline, Transfer = transfer };

    public static Orders Attack(int seat, Hex tile, IReadOnlyList<Move> attackers, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Attack, tile, moves) { Attackers = attackers };

    public static Orders Attune(int seat, Hex tile, params IReadOnlyList<Move> moves) => new(seat, Stance.Attune, tile, moves);

    public static Orders Summon(int seat, Hex tile, UnitKind kind, int count, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Summon, tile, moves, kind, count);

    /// <summary>Picks a stance but aims it at nothing: only the moves happen.</summary>
    public static Orders Hold(int seat, params IReadOnlyList<Move> moves) => new(seat, Stance.Summon, null, moves);
}
