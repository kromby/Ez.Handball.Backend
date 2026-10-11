using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

// One placed player, enriched for display. Name/ClubName/Position/Price are null when the
// player can't be resolved in the current squad (e.g. a stale lineup referencing a sold player).
public sealed record LineupPlayer(
    string PlayerId,
    string? Name,
    string? ClubName,
    string? Position,
    PlayerPrice? Price,
    LineupRole Role,
    int? BenchOrder,
    string? PositionSecondary = null,
    string? AssignedPosition = null);

public sealed record LineupView(
    IReadOnlyList<LineupPlayer> Slots,
    double CaptainMultiplier,
    bool IsValid,
    IReadOnlyList<LineupViolation> Violations);

public static class LineupViewMapper
{
    public static LineupView Map(
        IReadOnlyList<SquadPlayer> owned,
        Lineup lineup,
        LineupConstraints constraints,
        LineupValidation validation)
    {
        var byId = owned.ToDictionary(p => p.PlayerId);
        // #108: starters take the slot the lineup solve gives them (highest-rated placed first, so
        // it's stable); bench players and unsolvable lineups show the primary.
        var starterCandidates = lineup.Slots
            .Where(s => s.Role != LineupRole.Bench && byId.ContainsKey(s.PlayerId))
            .Select(s => byId[s.PlayerId])
            .OrderByDescending(p => p.Rating).ThenBy(p => p.PlayerId, StringComparer.Ordinal)
            .Select(p => new PositionCandidate(p.PlayerId, p.EligiblePositions))
            .ToList();
        var assigned = PositionAssignment.TrySolve(starterCandidates, constraints.PositionStart);

        var slots = lineup.Slots.Select(s =>
        {
            byId.TryGetValue(s.PlayerId, out var p);
            return new LineupPlayer(
                PlayerId: s.PlayerId,
                Name: p?.Name,
                ClubName: p?.ClubName,
                Position: p?.Position,
                Price: p?.Price,
                Role: s.Role,
                BenchOrder: s.BenchOrder,
                PositionSecondary: p?.PositionSecondary,
                AssignedPosition: assigned is not null && assigned.TryGetValue(s.PlayerId, out var pos) ? pos ?? p?.Position : p?.Position);
        }).ToList();

        return new LineupView(slots, constraints.CaptainMultiplier, validation.IsValid, validation.Violations);
    }
}
