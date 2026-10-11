namespace Ez.Handball.Domain;

// A player and the positions they may be counted under; Eligible[0] is the primary. An empty
// list means the position is unknown and the player never blocks a limit.
public sealed record PositionCandidate(string PlayerId, IReadOnlyList<string> Eligible);

// Backend#108: a player may play their primary or secondary position, but counts against only
// one. Instead of counting primaries, rules ask whether *some* assignment of players to eligible
// positions meets every (min, max). Squads are <=15 players with <=2 options each, so plain
// backtracking (primary first) is instant and yields a deterministic, primary-preferring answer.
public static class PositionAssignment
{
    public static IReadOnlyList<string> EligibleFor(string? position, string? positionSecondary)
    {
        if (string.IsNullOrWhiteSpace(position)) return Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(positionSecondary)
            || string.Equals(position, positionSecondary, StringComparison.Ordinal))
            return new[] { position };
        return new[] { position, positionSecondary };
    }

    public static IReadOnlyDictionary<string, (int Min, int Max)> MaxOnly(IReadOnlyDictionary<string, int> maxByPosition)
        => maxByPosition.ToDictionary(kv => kv.Key, kv => (0, kv.Value), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, (int Min, int Max)> MaxOnly(IReadOnlyDictionary<string, (int Min, int Max)> limits)
        => limits.ToDictionary(kv => kv.Key, kv => (0, kv.Value.Max), StringComparer.Ordinal);

    // playerId -> assigned position (null for unconstrained players), or null when no assignment
    // meets every limit. Positions absent from `limits` have no min and no max. `limits` is looked
    // up with its own comparer, so callers pass ordinal-keyed dictionaries (MaxOnly does).
    public static IReadOnlyDictionary<string, string?>? TrySolve(
        IReadOnlyList<PositionCandidate> players,
        IReadOnlyDictionary<string, (int Min, int Max)> limits)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var assigned = new string?[players.Count];
        if (!Assign(0)) return null;

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < players.Count; i++) result[players[i].PlayerId] = assigned[i];
        return result;

        bool Assign(int i)
        {
            // Prune: the players left can't cover what the mins still need.
            if (MinDeficit() > players.Count - i) return false;
            if (i == players.Count) return true;

            var eligible = players[i].Eligible;
            if (eligible.Count == 0)
            {
                assigned[i] = null;
                return Assign(i + 1);
            }

            foreach (var position in eligible)
            {
                var count = counts.GetValueOrDefault(position);
                if (limits.TryGetValue(position, out var limit) && count >= limit.Max) continue;

                counts[position] = count + 1;
                assigned[i] = position;
                if (Assign(i + 1)) return true;
                counts[position] = count;
            }
            return false;
        }

        int MinDeficit()
            => limits.Sum(kv => Math.Max(0, kv.Value.Min - counts.GetValueOrDefault(kv.Key)));
    }
}
