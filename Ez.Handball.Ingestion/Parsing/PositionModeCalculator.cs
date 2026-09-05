namespace Ez.Handball.Ingestion.Parsing;

// Primary = most-frequently-observed code; ties break by whichever code was observed earliest
// (deterministic without needing extra state). Secondary = the next most frequent code, but
// only if it accounts for more than 10% of total observations — otherwise there's no secondary.
public static class PositionModeCalculator
{
    public static (string Primary, string? Secondary) Compute(
        IReadOnlyList<(string Code, DateTimeOffset MatchDate)> observations)
    {
        if (observations.Count == 0)
            throw new ArgumentException("At least one observation is required.", nameof(observations));

        var ranked = observations
            .GroupBy(o => o.Code)
            .Select(g => new { Code = g.Key, Count = g.Count(), FirstSeen = g.Min(o => o.MatchDate) })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.FirstSeen)
            .ToList();

        var primary = ranked[0].Code;
        string? secondary = null;
        if (ranked.Count > 1 && (double)ranked[1].Count / observations.Count > 0.10)
            secondary = ranked[1].Code;

        return (primary, secondary);
    }
}
