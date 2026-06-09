namespace Ez.Handball.Domain;

public sealed record SquadSlot(
    string PlayerId,
    string? Position,
    PlayerPrice PricePaid);                          // price locked at buy time, NOT the current salary

public sealed record Squad(
    IReadOnlyList<SquadSlot> Players,
    double Budget,                                  // stored running cash balance
    string Currency);
