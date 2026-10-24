namespace Spellbound.Domain;

/// <summary>A seat's pick in the opening draft: one of the current Unveil's tiles still on the table.</summary>
public sealed record Pick(int Seat, Hex Tile);
