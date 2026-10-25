using System.Text.RegularExpressions;
using Spellbound.Domain;
using Match = Spellbound.Domain.Match;

namespace Spellbound.Web.Components.Game;

/// <summary>What an effect on the map shows (docs/theme.md, "Effects").</summary>
public enum EffectKind
{
    /// <summary>Units pop in with a ring.</summary>
    Summon,

    /// <summary>The army slides from its tile to the target, with a flash where they meet.</summary>
    Attack,

    /// <summary>A tile washes into its new owner's colour (an Attune, or an attack that took it).</summary>
    Take,

    /// <summary>A spell's glyph glows on its tile.</summary>
    Cast,
}

/// <summary>One effect on one tile: what, where (and for an attack, from where), and in whose colour.</summary>
public sealed record Effect(EffectKind Kind, Hex At, Hex? From, int Seat, string? Label = null);

/// <summary>
/// The effects of a playback beat, read from its report lines (the raw "Seat N …" lines, with tiles as coordinates):
/// a summon, an attune, a spell, an attack and whoever took the tile. The map plays them as the beat appears. A line
/// the fog hid isn't in the report, so its effect doesn't play either.
/// </summary>
public static partial class BeatEffects
{
    [GeneratedRegex(@"^Seat (\d+) summons (\d+)")]
    private static partial Regex Summons();

    [GeneratedRegex(@"^Seat (\d+) attunes ")]
    private static partial Regex Attunes();

    [GeneratedRegex(@"^Seat (\d+) casts ")]
    private static partial Regex Casts();

    [GeneratedRegex(@"^Seat (\d+) \([^)]*\) attack ")]
    private static partial Regex Attacks();

    [GeneratedRegex(@"^Seat (\d+) takes ")]
    private static partial Regex Takes();

    public static IReadOnlyList<Effect> Of(string beat, Match match)
    {
        ArgumentNullException.ThrowIfNull(beat);
        ArgumentNullException.ThrowIfNull(match);
        var effects = new List<Effect>();
        foreach (var line in beat.Split('\n'))
        {
            if (TileNames.Of(line).Cast<Hex?>().FirstOrDefault() is not { } at)
            {
                continue;
            }

            if (Summons().Match(line) is { Success: true } summon)
            {
                effects.Add(new Effect(EffectKind.Summon, at, null, Seat(summon), "+" + summon.Groups[2].Value));
            }
            else if (Attunes().Match(line) is { Success: true } attune)
            {
                effects.Add(new Effect(EffectKind.Take, at, null, Seat(attune)));
            }
            else if (Casts().Match(line) is { Success: true } cast)
            {
                effects.Add(new Effect(EffectKind.Cast, at, null, Seat(cast)));
            }
            else if (Attacks().Match(line) is { Success: true } attack)
            {
                // The army came from a tile of the attacker's next to the target (the first, as the board stands now).
                var seat = Seat(attack);
                var from = match.Map.NeighboursOf(at).Where(h => h != at && match.OwnerOf(h) == seat)
                    .OrderBy(h => h.Q).ThenBy(h => h.R).Cast<Hex?>().FirstOrDefault();
                effects.Add(new Effect(EffectKind.Attack, at, from, seat));
            }
            else if (Takes().Match(line) is { Success: true } take)
            {
                effects.Add(new Effect(EffectKind.Take, at, null, Seat(take)));
            }
        }

        return effects;
    }

    private static int Seat(System.Text.RegularExpressions.Match m) => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 1;
}
