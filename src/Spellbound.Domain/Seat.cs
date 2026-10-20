namespace Spellbound.Domain;

/// <summary>One player's place in a match: their mana, the tiles they hold and their banked points. Seats are
/// numbered from 0.</summary>
public sealed class Seat
{
    private readonly HashSet<Hex> tiles = [];

    public Seat(int index, int mana)
    {
        Index = index;
        Mana = mana;
    }

    public int Index { get; }

    public int Mana { get; internal set; }

    public IReadOnlySet<Hex> Tiles => tiles;

    /// <summary>Points banked so far; they are never taken away.</summary>
    public int Score { get; internal set; }

    /// <summary>Mana produced this round, scored at the round's end.</summary>
    public int ProducedThisRound { get; internal set; }

    /// <summary>Rival tiles taken and monster tiles cleared this round, scored at the round's end.</summary>
    public int ConquestsThisRound { get; internal set; }

    /// <summary>Produced mana not yet worth a point, carried to the next round's scoring.</summary>
    public int ProducedCarry { get; internal set; }

    /// <summary>Tile-rounds not yet worth a point, carried to the next round's scoring.</summary>
    public int LandCarry { get; internal set; }

    internal void Take(Hex hex) => tiles.Add(hex);

    internal void Lose(Hex hex) => tiles.Remove(hex);
}
