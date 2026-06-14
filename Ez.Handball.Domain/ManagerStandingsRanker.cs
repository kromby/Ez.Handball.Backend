namespace Ez.Handball.Domain;

// Pure ranking core. Given settled-score summaries (already scoped to the desired
// manager set) and a teamId → (name, color) map, produces the full ranked standings
// with movement vs the previous round. Ties share a rank (FPL-style); display order
// within a tie is by team name then teamId.
public static class ManagerStandingsRanker
{
    public static RankedManagers Rank(
        IEnumerable<GameweekScoreSummary> summaries,
        IReadOnlyDictionary<string, (string Name, string Color)> names)
    {
        var byTeam = summaries
            .GroupBy(s => s.TeamId)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (byTeam.Count == 0)
            return new RankedManagers(null, Array.Empty<ManagerStanding>());

        var rounds = byTeam.Values
            .SelectMany(rows => rows.Select(r => r.RoundLabel))
            .Distinct()
            .OrderBy(RoundOrder.Key)
            .ThenBy(r => r, StringComparer.Ordinal)
            .ToList();

        var latestRound = rounds[^1];
        string? previousRound = rounds.Count > 1 ? rounds[^2] : null;

        var totals = byTeam.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(r => r.Points));
        var roundPoints = byTeam.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(r => r.RoundLabel == latestRound).Sum(r => r.Points));

        // Previous ranking: totals over rounds ≤ previousRound, only for teams present by then.
        var previousRanks = previousRound is null
            ? new Dictionary<string, int>()
            : RankByTotal(
                byTeam
                    .Select(kv => (TeamId: kv.Key,
                        Total: kv.Value.Where(r => RoundOrder.Compare(r.RoundLabel, previousRound) <= 0).Sum(r => r.Points),
                        Present: kv.Value.Any(r => RoundOrder.Compare(r.RoundLabel, previousRound) <= 0)))
                    .Where(x => x.Present)
                    .ToDictionary(x => x.TeamId, x => x.Total),
                names);

        var ordered = Order(totals.Keys, totals, names);
        var currentRanks = AssignRanks(ordered, totals);

        var entries = new List<ManagerStanding>(ordered.Count);
        foreach (var teamId in ordered)
        {
            var rank = currentRanks[teamId];
            var total = totals[teamId];

            int? prevRank = previousRanks.TryGetValue(teamId, out var pr) ? pr : null;
            int? delta = prevRank is null ? null : prevRank - rank;
            var (name, color) = NameOrFallback(teamId, names);

            entries.Add(new ManagerStanding(
                Rank: rank,
                PreviousRank: prevRank,
                RankDelta: delta,
                TeamId: teamId,
                TeamName: name,
                Color: color,
                TotalPoints: total,
                RoundPoints: roundPoints[teamId]));
        }

        return new RankedManagers(latestRound, entries);
    }

    private static Dictionary<string, int> RankByTotal(
        IReadOnlyDictionary<string, double> totals,
        IReadOnlyDictionary<string, (string Name, string Color)> names)
    {
        var ordered = Order(totals.Keys, totals, names);
        return AssignRanks(ordered, totals);
    }

    // Assigns FPL-style shared ranks to an already-ordered team list:
    // equal totals share a rank, and the next distinct total jumps to its ordinal position
    // (two tied for 1st → next is 3rd).
    private static Dictionary<string, int> AssignRanks(
        IReadOnlyList<string> ordered,
        IReadOnlyDictionary<string, double> totals)
    {
        var ranks = new Dictionary<string, int>(ordered.Count);
        int rank = 0, seen = 0;
        double? lastTotal = null;
        foreach (var teamId in ordered)
        {
            seen++;
            var total = totals[teamId];
            // Totals are summed identically per team, so exact double comparison is intentional here.
            if (lastTotal is null || total != lastTotal) rank = seen;
            lastTotal = total;
            ranks[teamId] = rank;
        }
        return ranks;
    }

    private static List<string> Order(
        IEnumerable<string> teamIds,
        IReadOnlyDictionary<string, double> totals,
        IReadOnlyDictionary<string, (string Name, string Color)> names)
        => teamIds
            .OrderByDescending(t => totals[t])
            .ThenBy(t => DisplayName(t, names), StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t, StringComparer.Ordinal)
            .ToList();

    private static string DisplayName(string teamId, IReadOnlyDictionary<string, (string Name, string Color)> names)
        => names.TryGetValue(teamId, out var v) && !string.IsNullOrWhiteSpace(v.Name) ? v.Name : teamId;

    private static (string Name, string Color) NameOrFallback(
        string teamId, IReadOnlyDictionary<string, (string Name, string Color)> names)
        => names.TryGetValue(teamId, out var v)
            ? (string.IsNullOrWhiteSpace(v.Name) ? teamId : v.Name, v.Color ?? "")
            : (teamId, "");
}
