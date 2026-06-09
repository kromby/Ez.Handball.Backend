namespace Ez.Handball.Domain;

// One entry in the priced player pool ("transfer market"). PickPercentage is
// reserved — always null until the ownership aggregation follow-up ships.
public sealed record PlayerPoolEntry(
    int Rank,
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    PlayerPrice Price,
    double Rating,
    double? PickPercentage);

public sealed record PlayerPool(
    string Sort,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<PlayerPoolEntry> Entries);
