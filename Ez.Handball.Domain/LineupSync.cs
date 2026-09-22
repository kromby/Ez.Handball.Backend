namespace Ez.Handball.Domain;

// Keeps a saved lineup in step with the squad when a player is bought or sold (Backend#144),
// so settlement never scores a sold player or skips a new one.
public static class LineupSync
{
    // Drops the sold player's slot — and with it any armband they wore — and renumbers the bench
    // so its priority still runs contiguously from 0.
    public static Lineup WithoutPlayer(Lineup lineup, string playerId)
    {
        if (lineup.Slots.All(s => s.PlayerId != playerId)) return lineup;

        var remaining = lineup.Slots.Where(s => s.PlayerId != playerId).ToList();
        var benchRank = remaining
            .Where(s => s.Role == LineupRole.Bench)
            .OrderBy(s => s.BenchOrder)
            .Select((s, order) => (s.PlayerId, order))
            .ToDictionary(x => x.PlayerId, x => x.order, StringComparer.Ordinal);

        return new Lineup(remaining
            .Select(s => s.Role == LineupRole.Bench ? s with { BenchOrder = benchRank[s.PlayerId] } : s)
            .ToList());
    }

    // The bought player starts; squads are one-per-position at StarterCount, so a buy always
    // fills the slot a sale freed.
    public static Lineup WithStarter(Lineup lineup, string playerId)
    {
        if (lineup.Slots.Any(s => s.PlayerId == playerId)) return lineup;
        return new Lineup(lineup.Slots.Append(new LineupSlot(playerId, LineupRole.Starter, null)).ToList());
    }
}
