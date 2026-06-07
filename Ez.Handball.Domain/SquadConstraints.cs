namespace Ez.Handball.Domain;

public sealed record SquadConstraints(
    int Version,
    int MaxSquadSize,
    IReadOnlyDictionary<string, int> PositionLimits,   // position -> max; empty => unenforced
    double StartingCap,                                // a new manager's starting cash
    string Currency,                                   // e.g. "ISK"
    double SellOnFeeRate = 0.5);                        // FPL-style sell-on fee: fraction of an owned player's PROFIT (current price − price paid) withheld on sale; 0.5 = seller keeps half the gain
