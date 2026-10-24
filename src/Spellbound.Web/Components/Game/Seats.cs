using System.Text.RegularExpressions;
using Match = Spellbound.Domain.Match;
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

    /// <summary>
    /// A report line in the player's words: their seat becomes "you" (with the verb to match: "Seat 1 wins" reads "You
    /// win"), and bots are "Bot N". The domain writes "Seat N" so its lines read the same for everyone.
    /// </summary>
    public static string Phrase(string line, IReadOnlyList<string?> owners, int? you)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(owners);
        if (you is { } me)
        {
            var seat = $"Seat {me + 1}";
            line = Regex.Replace(line, $@"\b{seat}'s\b", "your");
            line = Regex.Replace(line, $@"\b{seat} (wins|gets|moves|summons|collects|pays|banks|attunes|takes|casts|chooses|builds|reinforces|has|passes)\b",
                m => "You " + m.Groups[1].Value switch { "has" => "have", "passes" => "pass", var verb => verb[..^1] });
            line = Regex.Replace(line, $@"\b{seat}\b", "you");
        }

        for (var i = 0; i < owners.Count; i++)
        {
            if (owners[i] is null && i != you)
            {
                line = Regex.Replace(line, $@"\bSeat {i + 1}\b", $"Bot {i + 1}");
            }
        }

        // Capitalise the start of each sentence ("you collect" at the start of a line, "The match is over. you win").
        return Regex.Replace(line, @"(^|[.:!?] )(you|your)\b", m => m.Groups[1].Value + char.ToUpperInvariant(m.Groups[2].Value[0]) + m.Groups[2].Value[1..]);
    }

    /// <summary>A tile type's symbol, on the map and before a tile in the lists: ◆ Steady, ≈ Shifting, ✶ Wild.</summary>
    public static string Mark(TileType type) => type switch
    {
        TileType.Steady => "◆",
        TileType.Shifting => "≈",
        _ => "✶",
    };

    /// <summary>What a tile is, in a few words, for its label and the tile panel; with its type's
    /// <paramref name="mark"/> first, except where a screen reader would read the symbol out.</summary>
    public static string Describe(Match match, Hex hex, int? you, IReadOnlyList<string?>? owners = null, bool mark = true, bool named = true)
    {
        var tile = match.Map[hex];
        if (!tile.IsUnveiled)
        {
            return named ? $"Dark tile {TileNames.Of(match.Map, hex)}" : "Dark tile";
        }

        var owner = match.OwnerOf(hex) is { } o ? (o == you ? "yours" : owners?[o] is null && owners is not null ? $"Bot {o + 1}'s" : $"seat {o + 1}'s") : tile.IsGuarded ? $"guarded by monsters (power {tile.Guard})" : "free";
        // The fog of war: a rival's units far from your land are unseen.
        var seen = you is { } viewer ? match.UnitsSeenBy(viewer, hex) : match.UnitsOn(hex);
        var units = seen is { IsEmpty: false } army ? $", {army}" : seen is null && match.OwnerOf(hex) is not null ? ", units unseen" : "";
        var built = match.UpgradeOn(hex) is { } upgrade ? $", with a {upgrade}" : "";
        return $"{(mark ? Mark(tile.Type) + " " : "")}{tile.Type} tile{(named ? " " + TileNames.Of(match.Map, hex) : "")}, yields {match.Rules.YieldRange(tile.Type)}, {owner}{built}{units}";
    }
}
