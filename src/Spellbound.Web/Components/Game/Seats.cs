using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web.Components.Game;

/// <summary>How seats look on the match pages: each seat in one of the theme's five inks (docs/theme.md), and named
/// "you" or "Bot N" for the player looking.</summary>
public static class Seats
{
    private static readonly string[] Inks = ["radiant", "ember", "tidal", "verdant", "umbral"];

    /// <summary>The CSS ink of a seat, for example "var(--radiant-ink)".</summary>
    public static string Ink(int seat) => $"var(--{Inks[seat % Inks.Length]}-ink)";

    public static string Name(LoadedMatch match, int seat, int? you) =>
        seat == you ? "You" : match.Owners[seat] is null ? $"Bot {seat + 1}" : $"Seat {seat + 1}";

    /// <summary>A report line with the player's own seat marked, so they can find themselves in it.</summary>
    public static string Mark(string line, int? you) =>
        you is { } seat ? line.Replace($"Seat {seat + 1} ", $"Seat {seat + 1} (you) ", StringComparison.Ordinal) : line;

    /// <summary>What a tile is, in a few words, for its label and the tile panel.</summary>
    public static string Describe(Match match, Hex hex, int? you)
    {
        var tile = match.Map[hex];
        if (!tile.IsUnveiled)
        {
            return $"Dark tile at {hex}";
        }

        var owner = match.OwnerOf(hex) is { } o ? (o == you ? "yours" : $"seat {o + 1}'s") : tile.IsGuarded ? $"guarded by monsters (power {tile.Guard})" : "free";
        var units = match.UnitsOn(hex) is { IsEmpty: false } army ? $", {army}" : "";
        return $"{tile.Type} tile at {hex}, yields {tile.Yield}, {owner}{units}";
    }
}
