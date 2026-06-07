namespace Ez.Handball.Domain;

public sealed record SquadConstraints(
    int Version,
    int MaxSquadSize,
    IReadOnlyDictionary<string, int> PositionLimits,   // position -> max; empty => unenforced
    double StartingCap,                                // a new manager's starting cash
    string Currency);                                  // e.g. "ISK"
