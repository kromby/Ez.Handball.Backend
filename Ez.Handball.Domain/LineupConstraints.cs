namespace Ez.Handball.Domain;

public sealed record LineupConstraints(
    int Version,
    int StarterCount,                                                   // 7
    IReadOnlyDictionary<string, (int Min, int Max)> PositionStart,      // per-position min/max among starters
    double CaptainMultiplier,                                           // 2.0 — stored here, applied by #60
    bool CaptainRequired,                                               // true
    bool ViceRequired);                                                 // false
