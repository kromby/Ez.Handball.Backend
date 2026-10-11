namespace Ez.Handball.Domain;

// How many players a team bought and sold in transfers that applied to one round.
public sealed record RoundTransferCount(string RoundLabel, int Buys, int Sells);

// A team's transfer position for one round. Used = transfers counted against the free ones;
// Extra = transfers beyond them, each costing PointsPerExtraTransfer. Exempt rounds (squad
// building, before the rules start, wildcard) never cost points.
public sealed record TransferAllowance(
    string RoundLabel,
    int RoundNumber,
    int FreeAvailable,
    int Used,
    int Extra,
    int PointsHit,
    bool Exempt);

public static class TransferAllowanceCalculator
{
    // Walks the calendar up to roundLabel, carrying unused free transfers forward. Null when the
    // round isn't in the calendar. A team that didn't exist at the previous round's deadline is
    // still building its squad (same CreatedAt-vs-deadline rule as #158), so that round is free.
    public static TransferAllowance? Compute(
        TransferConfig config,
        IReadOnlyList<Gameweek> calendar,
        IReadOnlyList<RoundTransferCount> counts,
        DateTimeOffset? teamCreatedAt,
        string roundLabel,
        IReadOnlySet<string>? wildcardRounds)
    {
        var countsByRound = counts.ToDictionary(c => c.RoundLabel, StringComparer.Ordinal);
        var ordered = calendar.OrderBy(g => g.Number).ToList();
        var banked = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var round = ordered[i];
            var building = i == 0
                || round.Number < config.StartsFromRound
                || teamCreatedAt >= ordered[i - 1].Deadline;
            var wildcard = wildcardRounds?.Contains(round.RoundLabel) == true;

            var free = config.FreeTransfersPerRound + (building ? 0 : banked);
            var used = countsByRound.TryGetValue(round.RoundLabel, out var c) ? Math.Max(c.Buys, c.Sells) : 0;
            var exempt = building || wildcard;

            if (round.RoundLabel == roundLabel)
            {
                var extra = exempt ? 0 : Math.Max(0, used - free);
                return new TransferAllowance(
                    round.RoundLabel, round.Number, free, used, extra, extra * config.PointsPerExtraTransfer, exempt);
            }

            // Building rounds start the bank from zero. A wildcard round spends nothing, so its
            // free transfers carry over as if unused.
            banked = building
                ? 0
                : Math.Min(config.MaxBankedFreeTransfers, Math.Max(0, free - (wildcard ? 0 : used)));
        }

        return null;
    }
}
