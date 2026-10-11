using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

// Shared glue for both standings use cases: build the manager name/color map and
// paginate the ranked list. The ranking itself lives in ManagerStandingsRanker.
internal static class ManagerStandingsAssembly
{
    public static async Task<IReadOnlyDictionary<string, (string Name, string Color)>> NameMapAsync(
        IGameTeamRepository teams, CancellationToken ct)
    {
        var all = await teams.ListByFlavorAsync(GameFlavor.Fantasy, ct);
        return all.ToDictionary(t => t.TeamId, t => (t.Name, t.Color));
    }

    // Drops rounds before a mini-league's start round, so totals, rounds played, average and
    // history all cover only the rounds the league counts. Null start round keeps every round.
    public static IEnumerable<GameweekScoreSummary> FromStartRound(
        IEnumerable<GameweekScoreSummary> summaries, string? startRound)
        => startRound is null
            ? summaries
            : summaries.Where(s => RoundOrder.Compare(s.RoundLabel, startRound) >= 0);

    public static ManagerStandings Paginate(RankedManagers ranked, int offset, int limit)
        => new(
            Total: ranked.Entries.Count,
            Offset: offset,
            Limit: limit,
            LatestRoundLabel: ranked.LatestRoundLabel,
            Entries: ranked.Entries.Skip(offset).Take(limit).ToList());
}
