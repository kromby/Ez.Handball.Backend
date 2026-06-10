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
    int? BenchOrder);

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
                BenchOrder: s.BenchOrder);
        }).ToList();

        return new LineupView(slots, constraints.CaptainMultiplier, validation.IsValid, validation.Violations);
    }
}
