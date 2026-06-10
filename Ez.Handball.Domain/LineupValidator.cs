namespace Ez.Handball.Domain;

public static class LineupValidator
{
    public static LineupValidation Validate(
        Lineup proposed,
        IReadOnlyList<SquadPlayer> ownedSquad,
        LineupConstraints constraints)
    {
        var violations = new List<LineupViolation>();
        var slots = proposed.Slots;
        var ownedById = ownedSquad.ToDictionary(p => p.PlayerId);
        var starters = slots.Where(IsStarter).ToList();

        CheckMembership(slots, ownedById, violations);
        CheckStarterCount(starters, constraints, violations);
        CheckPositions(starters, ownedById, constraints, violations);
        CheckCaptaincy(slots, constraints, violations);
        CheckBenchOrder(slots, starters, violations);

        return new LineupValidation(violations.Count == 0, violations);
    }

    // Captain and Vice ARE starters carrying the badge.
    private static bool IsStarter(LineupSlot s)
        => s.Role is LineupRole.Starter or LineupRole.Captain or LineupRole.Vice;

    // duplicate_slot, unowned_player, incomplete_squad
    private static void CheckMembership(
        IReadOnlyList<LineupSlot> slots,
        IReadOnlyDictionary<string, SquadPlayer> ownedById,
        List<LineupViolation> violations)
    {
        var seen = new HashSet<string>();
        if (slots.Any(s => !seen.Add(s.PlayerId)))
            violations.Add(new("duplicate_slot", "A player appears more than once in the lineup."));

        if (slots.Any(s => !ownedById.ContainsKey(s.PlayerId)))
            violations.Add(new("unowned_player", "The lineup references a player not in the owned squad."));

        // lineup ids must equal owned ids exactly — every owned player placed once, nothing extra
        if (!slots.Select(s => s.PlayerId).ToHashSet().SetEquals(ownedById.Keys))
            violations.Add(new("incomplete_squad", "Every owned player must be placed in the lineup exactly once."));
    }

    private static void CheckStarterCount(
        IReadOnlyList<LineupSlot> starters, LineupConstraints constraints, List<LineupViolation> violations)
    {
        if (starters.Count != constraints.StarterCount)
            violations.Add(new("wrong_starter_count", $"A lineup must have exactly {constraints.StarterCount} starters."));
    }

    // position_min / position_max among starters — position resolved from the owned roster snapshot
    private static void CheckPositions(
        IReadOnlyList<LineupSlot> starters,
        IReadOnlyDictionary<string, SquadPlayer> ownedById,
        LineupConstraints constraints,
        List<LineupViolation> violations)
    {
        var startersByPosition = starters
            .Select(s => ownedById.TryGetValue(s.PlayerId, out var p) ? p.Position : null)
            .Where(pos => pos is not null)
            .GroupBy(pos => pos!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var kv in constraints.PositionStart)
        {
            var (min, max) = kv.Value;
            startersByPosition.TryGetValue(kv.Key, out var count);
            if (count < min)
                violations.Add(new("position_min", $"At least {min} starter(s) required at {kv.Key}."));
            if (count > max)
                violations.Add(new("position_max", $"At most {max} starter(s) allowed at {kv.Key}."));
        }
    }

    // missing_captain / multiple_captains / missing_vice / multiple_vices
    private static void CheckCaptaincy(
        IReadOnlyList<LineupSlot> slots, LineupConstraints constraints, List<LineupViolation> violations)
    {
        var captains = slots.Count(s => s.Role == LineupRole.Captain);
        if (constraints.CaptainRequired && captains == 0)
            violations.Add(new("missing_captain", "A captain must be selected."));
        if (captains > 1)
            violations.Add(new("multiple_captains", "Only one captain may be selected."));

        var vices = slots.Count(s => s.Role == LineupRole.Vice);
        if (constraints.ViceRequired && vices == 0)
            violations.Add(new("missing_vice", "A vice-captain must be selected."));
        if (vices > 1)
            violations.Add(new("multiple_vices", "Only one vice-captain may be selected."));
    }

    // bench_order: bench orders contiguous 0..n-1, no nulls; starters carry no order
    private static void CheckBenchOrder(
        IReadOnlyList<LineupSlot> slots, IReadOnlyList<LineupSlot> starters, List<LineupViolation> violations)
    {
        var bench = slots.Where(s => s.Role == LineupRole.Bench).ToList();
        var benchOk = bench.All(b => b.BenchOrder is not null)
            && bench.Select(b => b.BenchOrder!.Value).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, bench.Count));
        var startersOk = starters.All(s => s.BenchOrder is null);
        if (!benchOk || !startersOk)
            violations.Add(new("bench_order", "Bench priority must run contiguously from 0 and starters must carry no bench order."));
    }
}
