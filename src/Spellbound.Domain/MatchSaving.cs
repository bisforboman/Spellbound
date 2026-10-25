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
        [.. toPick],
        [.. Map.Tiles.OrderBy(t => t.Hex.Q).ThenBy(t => t.Hex.R).Select(t => new TileState(t.Hex, t.Type, t.Yield, t.Guard))],
        [.. seats.Select(s => new SeatState(
            s.Mana, s.Score, s.ProducedThisRound, s.ProducedCarry, s.ConquestPointsThisRound,
            [.. s.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R)], s.LandCarry,
            [.. s.Offer], s.Trait, s.TraitPoints, [.. s.OpeningTiles], s.MonstersCleared, s.RivalTilesTaken,
            [.. s.SpellsCast.Order()], s.HexesCast, s.RoundsCast, s.LastRoundCast))],
        [.. units.Where(u => !u.Value.IsEmpty).OrderBy(u => u.Key.Q).ThenBy(u => u.Key.R)
            .Select(u => new UnitsState(u.Key, u.Value.Imps, u.Value.Golems))],
        [.. upgrades.OrderBy(u => u.Key.Q).ThenBy(u => u.Key.R).Select(u => new UpgradeState(u.Key, u.Value))],
        [.. blightedLastRound.OrderBy(h => h.Q).ThenBy(h => h.R)],
        [.. picks],
        acted,
        [.. wards.OrderBy(w => w.Key.Q).ThenBy(w => w.Key.R).Select(w => new WardState(w.Key, w.Value))],
        [.. blights.OrderBy(h => h.Q).ThenBy(h => h.R)],
        offer,
        [.. pacified.Select(p => (p.Key, Army: PacifiedOn(p.Key))).Where(p => !p.Army.IsEmpty).OrderBy(p => p.Key.Q).ThenBy(p => p.Key.R)
            .Select(p => new UnitsState(p.Key, p.Army.Imps, p.Army.Golems))]);

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
                LandCarry = s.LandCarry,
                ConquestPointsThisRound = s.ConquestsThisRound,
                Offer = s.Offer ?? [],
                Trait = s.Trait,
                TraitPoints = s.TraitPoints,
                OpeningTiles = s.OpeningTiles ?? [],
                MonstersCleared = s.MonstersCleared,
                RivalTilesTaken = s.RivalTilesTaken,
            };
            foreach (var spell in s.SpellsCast ?? [])
            {
                seat.Cast(spell, 0);
            }

            seat.HexesCast = s.HexesCast;
            seat.RoundsCast = s.RoundsCast;
            seat.LastRoundCast = s.LastRoundCast;
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
        match.toPick.AddRange(state.OnAuction);
        foreach (var u in state.Units)
        {
            match.units[u.Hex] = new Army(u.Imps, u.Golems);
        }

        foreach (var u in state.Upgrades ?? [])
        {
            match.upgrades[u.Hex] = u.Upgrade;
        }

        match.blightedLastRound.UnionWith(state.Blighted ?? []);
        match.picks.AddRange(state.Picks ?? []);
        match.acted = state.Acted;
        foreach (var w in state.Wards ?? [])
        {
            match.wards[w.Hex] = w.Power;
        }

        match.blights.UnionWith(state.Blights ?? []);
        match.offer = state.Offer;
        foreach (var p in state.Pacified ?? [])
        {
            match.pacified[p.Hex] = new Army(p.Imps, p.Golems);
        }

        return match;
    }
}
