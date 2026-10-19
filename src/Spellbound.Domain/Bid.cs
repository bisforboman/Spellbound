namespace Spellbound.Domain;

/// <summary>A seat's sealed bid in an opening auction: mana on one of the auctioned tiles, or a pass (no tile).</summary>
public sealed record Bid(int Seat, Hex? Tile, int Amount)
{
    public static Bid Pass(int seat) => new(seat, null, 0);
}
