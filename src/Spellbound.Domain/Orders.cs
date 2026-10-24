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

    /// <summary>Spend mana to build an upgrade on one of your tiles.</summary>
    Build,

    /// <summary>Spend mana to cast one spell.</summary>
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

    /// <summary>No longer cast (removed 24 Oct 2026, when Cast became a stance and a round could no longer cast and
    /// attack). Kept so saved matches that cast it still load; it still counts as a spell cast for Spellweaver.</summary>
    Rally,
}

/// <summary>Units summoned on one of a seat's own tiles alongside an attack, to guard it while the army is away.</summary>
public sealed record Reinforcement(Hex Tile, UnitKind Kind, int Count);

/// <summary>Units moving one step, from one of a player's tiles to a neighbouring one of theirs.</summary>
public sealed record Move(Hex From, Hex To, Army Units);

/// <summary>
/// A seat's secret commitment for a round: one stance, what it aims at, and any moves. A stance with no target does
/// nothing (a player can always hold back). For Summon, <see cref="Kind"/> and <see cref="Count"/> say what to summon;
/// for Attack, <see cref="Attackers"/> says which units go, from which of the seat's tiles next to the target. For
/// Cast, <see cref="Spell"/> says which spell, <see cref="Count"/> how much power a Scorch burns, and
/// <see cref="Transfer"/> what a Leyline moves (the target is where its units leave from).
/// </summary>
public sealed record Orders(int Seat, Stance Stance, Hex? Target, IReadOnlyList<Move> Moves, UnitKind Kind = UnitKind.Imp, int Count = 0)
{
    public IReadOnlyList<Move> Attackers { get; init; } = [];

    /// <summary>For Build: what to build.</summary>
    public Upgrade Upgrade { get; init; }

    /// <summary>For Cast: which spell.</summary>
    public Spell Spell { get; init; }

    /// <summary>For a Leyline: the units it moves, and between which tiles.</summary>
    public Move? Transfer { get; init; }

    /// <summary>For Attack: units summoned on one of the seat's tiles at the same time, if any.</summary>
    public Reinforcement? Reinforce { get; init; }

    /// <summary>These orders (an attack) with a small summon at home alongside.</summary>
    public Orders Reinforcing(Hex tile, UnitKind kind, int count) => this with { Reinforce = new Reinforcement(tile, kind, count) };

    public static Orders Attack(int seat, Hex tile, IReadOnlyList<Move> attackers, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Attack, tile, moves) { Attackers = attackers };

    public static Orders Attune(int seat, Hex tile, params IReadOnlyList<Move> moves) => new(seat, Stance.Attune, tile, moves);

    public static Orders Summon(int seat, Hex tile, UnitKind kind, int count, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Summon, tile, moves, kind, count);

    public static Orders Build(int seat, Hex tile, Upgrade upgrade, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Build, tile, moves) { Upgrade = upgrade };

    /// <summary>Casts a spell at a tile; for Scorch, <paramref name="count"/> is the power to burn.</summary>
    public static Orders Cast(int seat, Spell spell, Hex tile, int count = 0, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Cast, tile, moves, Count: count) { Spell = spell };

    /// <summary>Casts a Leyline: <paramref name="transfer"/>'s units travel between two of the seat's tiles.</summary>
    public static Orders Leyline(int seat, Move transfer, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Cast, (transfer ?? throw new ArgumentNullException(nameof(transfer))).From, moves)
        {
            Spell = Spell.Leyline,
            Transfer = transfer,
        };

    /// <summary>Picks a stance but aims it at nothing: only the moves happen.</summary>
    public static Orders Hold(int seat, params IReadOnlyList<Move> moves) => new(seat, Stance.Summon, null, moves);
}
