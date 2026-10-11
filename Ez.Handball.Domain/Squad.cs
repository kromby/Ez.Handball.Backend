namespace Ez.Handball.Domain;

public sealed record SquadSlot(
    string PlayerId,
    string? Position,
    PlayerPrice PricePaid,                           // price locked at buy time, NOT the player's current market price
    IReadOnlyList<string>? Eligible = null,          // live eligible positions (#108); null → just the snapshot Position
    string? PlayAs = null)                           // manager's pin (#166), carried for later snapshotting; EligiblePositions ignores it
{
    public IReadOnlyList<string> EligiblePositions => Eligible ?? PositionAssignment.EligibleFor(Position, null);
}

public sealed record Squad(
    IReadOnlyList<SquadSlot> Players,
    double Budget,                                  // stored running cash balance
    string Currency);
