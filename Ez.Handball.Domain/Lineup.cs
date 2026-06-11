namespace Ez.Handball.Domain;

// Captain and Vice ARE starters carrying the multiplier badge — collapsing role + captaincy
// into one enum makes "captain on the bench" and "captain == vice" unrepresentable.
public enum LineupRole
{
    Bench,
    Starter,
    Captain,
    Vice
}

public sealed record LineupSlot(
    string PlayerId,
    LineupRole Role,
    int? BenchOrder);    // 0-based bench priority; set iff Role == Bench, null otherwise

public sealed record Lineup(
    IReadOnlyList<LineupSlot> Slots);
