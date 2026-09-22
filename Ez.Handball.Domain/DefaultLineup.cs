namespace Ez.Handball.Domain;

// The lineup a manager plays with until they save one (Backend#142) — the Web has no lineup
// editor yet, so without this nobody would ever score. Highest-rated players start, as long as
// their position still has room under the per-position max; everyone else is benched in rating
// order. No captain: the armband is a deliberate choice, and scoring handles its absence.
// Deterministic (ties broken by playerId) so a re-settle freezes the same lineup.
public static class DefaultLineup
{
    public static Lineup From(IReadOnlyList<SquadPlayer> squad, LineupConstraints constraints)
    {
        var byRating = squad
            .OrderByDescending(p => p.Rating)
            .ThenBy(p => p.PlayerId, StringComparer.Ordinal)
            .ToList();

        var startersByPosition = new Dictionary<string, int>(StringComparer.Ordinal);
        var starters = new List<LineupSlot>();
        var bench = new List<SquadPlayer>();

        foreach (var player in byRating)
        {
            if (starters.Count < constraints.StarterCount && HasRoom(player.Position, startersByPosition, constraints))
            {
                starters.Add(new LineupSlot(player.PlayerId, LineupRole.Starter, null));
                if (player.Position is not null)
                    startersByPosition[player.Position] = startersByPosition.GetValueOrDefault(player.Position) + 1;
            }
            else
            {
                bench.Add(player);
            }
        }

        var benchSlots = bench.Select((player, order) => new LineupSlot(player.PlayerId, LineupRole.Bench, order));
        return new Lineup(starters.Concat(benchSlots).ToList());
    }

    // A position without a configured max (or an unknown position) never blocks a start.
    private static bool HasRoom(
        string? position, IReadOnlyDictionary<string, int> startersByPosition, LineupConstraints constraints)
        => position is null
           || !constraints.PositionStart.TryGetValue(position, out var limits)
           || startersByPosition.GetValueOrDefault(position) < limits.Max;
}
