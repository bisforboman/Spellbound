namespace Spellbound.Domain;

/// <summary>One player's place in a match: their mana and the tiles they hold. Seats are numbered from 0.</summary>
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

    internal void Take(Hex hex) => tiles.Add(hex);

    internal void Lose(Hex hex) => tiles.Remove(hex);
}
