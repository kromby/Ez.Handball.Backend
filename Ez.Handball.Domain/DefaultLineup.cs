namespace Ez.Handball.Domain;

// The lineup a manager plays with until they save one (Backend#142) — the Web has no lineup
// editor yet, so without this nobody would ever score. Highest-rated players start, as long as
// the starters can still be arranged within each position's max (primary or secondary, #108);
// everyone else is benched in rating order. No captain: the armband is a deliberate choice, and scoring handles its absence.
// Deterministic (ties broken by playerId) so a re-settle freezes the same lineup.
public static class DefaultLineup
{
    public static Lineup From(IReadOnlyList<SquadPlayer> squad, LineupConstraints constraints)
    {
        var byRating = squad
            .OrderByDescending(p => p.Rating)
            .ThenBy(p => p.PlayerId, StringComparer.Ordinal)
            .ToList();

        var maxOnly = PositionAssignment.MaxOnly(constraints.PositionStart);
        var starterCandidates = new List<PositionCandidate>();
        var starters = new List<LineupSlot>();
        var bench = new List<SquadPlayer>();

        foreach (var player in byRating)
        {
            var candidate = new PositionCandidate(player.PlayerId, player.EligiblePositions);
            if (starters.Count < constraints.StarterCount
                && PositionAssignment.TrySolve([.. starterCandidates, candidate], maxOnly) is not null)
            {
                starters.Add(new LineupSlot(player.PlayerId, LineupRole.Starter, null));
                starterCandidates.Add(candidate);
            }
            else
            {
                bench.Add(player);
            }
        }

        var benchSlots = bench.Select((player, order) => new LineupSlot(player.PlayerId, LineupRole.Bench, order));
        return new Lineup(starters.Concat(benchSlots).ToList());
    }
}
