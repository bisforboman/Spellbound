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

    /// <summary>Your attack this round strikes with more power: the attacker's Ward.</summary>
    Rally,
}

/// <summary>A spell cast alongside a round's stance: which spell, at which tile, how much power Scorch burns, and what
/// Leyline moves (its <see cref="Target"/> is where the units leave from).</summary>
public sealed record SpellCast(Spell Spell, Hex Target, int Count = 0, Move? Transfer = null);

/// <summary>Units summoned on one of a seat's own tiles alongside an attack, to guard it while the army is away.</summary>
public sealed record Reinforcement(Hex Tile, UnitKind Kind, int Count);

/// <summary>A guess at a rival's secret trait, made alongside a round's orders: once per rival, never changed, scored at
/// the end.</summary>
public sealed record TraitGuess(int Rival, Trait Trait);

/// <summary>Units moving one step, from one of a player's tiles to a neighbouring one of theirs.</summary>
public sealed record Move(Hex From, Hex To, Army Units);

/// <summary>
/// A seat's secret commitment for a round: one stance, what it aims at, and any moves. A stance with no target does
/// nothing (a player can always hold back). For Summon, <see cref="Kind"/> and <see cref="Count"/> say what to summon;
/// for Attack, <see cref="Attackers"/> says which units go, from which of the seat's tiles next to the target. Alongside
/// the stance a seat may cast one spell (<see cref="Cast"/>).
/// </summary>
public sealed record Orders(int Seat, Stance Stance, Hex? Target, IReadOnlyList<Move> Moves, UnitKind Kind = UnitKind.Imp, int Count = 0)
{
    public IReadOnlyList<Move> Attackers { get; init; } = [];

    /// <summary>For Build: what to build.</summary>
    public Upgrade Upgrade { get; init; }

    /// <summary>The spell cast alongside the stance this round, if any.</summary>
    public SpellCast? Cast { get; init; }

    /// <summary>For Attack: units summoned on one of the seat's tiles at the same time, if any.</summary>
    public Reinforcement? Reinforce { get; init; }

    /// <summary>These orders (an attack) with a small summon at home alongside.</summary>
    public Orders Reinforcing(Hex tile, UnitKind kind, int count) => this with { Reinforce = new Reinforcement(tile, kind, count) };

    /// <summary>A follow order: an Attune, Summon or Build in another stance, which happens only if a rival chose that
    /// stance this round, at a surcharge (docs/discussions/shared-stances.md).</summary>
    public Orders? Follow { get; init; }

    /// <summary>These orders with a follow order alongside.</summary>
    public Orders Following(Orders follow) => this with { Follow = follow };

    /// <summary>A guess at a rival's trait made alongside these orders, if any.</summary>
    public TraitGuess? Guess { get; init; }

    /// <summary>These orders with a guess at a rival's trait.</summary>
    public Orders Guessing(int rival, Trait trait) => this with { Guess = new TraitGuess(rival, trait) };

    /// <summary>These orders with a spell cast alongside.</summary>
    public Orders Casting(Spell spell, Hex tile, int count = 0) => this with { Cast = new SpellCast(spell, tile, count) };

    /// <summary>These orders with a Leyline cast alongside.</summary>
    public Orders CastingLeyline(Move transfer) =>
        this with { Cast = new SpellCast(Spell.Leyline, (transfer ?? throw new ArgumentNullException(nameof(transfer))).From, Transfer: transfer) };

    public static Orders Attack(int seat, Hex tile, IReadOnlyList<Move> attackers, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Attack, tile, moves) { Attackers = attackers };

    public static Orders Attune(int seat, Hex tile, params IReadOnlyList<Move> moves) => new(seat, Stance.Attune, tile, moves);

    public static Orders Summon(int seat, Hex tile, UnitKind kind, int count, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Summon, tile, moves, kind, count);

    public static Orders Build(int seat, Hex tile, Upgrade upgrade, params IReadOnlyList<Move> moves) =>
        new(seat, Stance.Build, tile, moves) { Upgrade = upgrade };

    /// <summary>Picks a stance but aims it at nothing: only the moves happen.</summary>
    public static Orders Hold(int seat, params IReadOnlyList<Move> moves) => new(seat, Stance.Summon, null, moves);
}
