namespace Spellbound.Domain;

/// <summary>Saving a match as a <see cref="MatchState"/> and loading it back.</summary>
public sealed partial class Match
{
    /// <summary>The match as plain data, everything in a fixed order so the same match always saves the same.</summary>
    public MatchState Save() => new(
        MatchState.CurrentVersion,
        Phase,
        OpeningUnveil,
        CurrentRound,
        [.. onAuction],
        [.. Map.Tiles.OrderBy(t => t.Hex.Q).ThenBy(t => t.Hex.R).Select(t => new TileState(t.Hex, t.Type, t.Yield, t.Guard))],
        [.. seats.Select(s => new SeatState(
            s.Mana, s.Score, s.ProducedThisRound, s.ProducedCarry, s.ConquestsThisRound,
            [.. s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R)]))],
        [.. units.Where(u => !u.Value.IsEmpty).OrderBy(u => u.Key.Q).ThenBy(u => u.Key.R)
            .Select(u => new UnitsState(u.Key, u.Value.Imps, u.Value.Golems))]);

    /// <summary>A saved match, to play on under the rules it was saved with.</summary>
    public static Match Load(MatchState state, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        if (state.Version != MatchState.CurrentVersion)
        {
            throw new NotSupportedException($"A saved match of version {state.Version} can't be read; this is version {MatchState.CurrentVersion}.");
        }

        var map = GameMap.FromTiles(state.Tiles.Select(t => new Tile(t.Hex, t.Type, t.Yield, t.Guard)));
        var seats = state.Seats.Select((s, i) =>
        {
            var seat = new Seat(i, s.Mana)
            {
                Score = s.Score,
                ProducedThisRound = s.ProducedThisRound,
                ProducedCarry = s.ProducedCarry,
                ConquestsThisRound = s.ConquestsThisRound,
            };
            foreach (var hex in s.Tiles)
            {
                seat.Take(hex);
            }

            return seat;
        }).ToList();
        var match = new Match(rules, map, seats)
        {
            Phase = state.Phase,
            OpeningUnveil = state.OpeningUnveil,
            CurrentRound = state.CurrentRound,
        };
        match.onAuction.AddRange(state.OnAuction);
        foreach (var u in state.Units)
        {
            match.units[u.Hex] = new Army(u.Imps, u.Golems);
        }

        return match;
    }
}
