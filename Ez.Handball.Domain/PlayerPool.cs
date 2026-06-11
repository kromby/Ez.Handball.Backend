namespace Ez.Handball.Domain;

// One entry in the priced player list. Carries real-life stats AND fantasy
// rating/price so a single table can sort by either. PickPercentage is
// reserved — always null until the ownership aggregation follow-up ships.
public sealed record PlayerPoolEntry(
    int Rank,
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    int Games,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards,
    double AvgGoals,
    PlayerPrice Price,
    double Rating,
    double? PickPercentage);

public sealed record PlayerPool(
    string Sort,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<PlayerPoolEntry> Entries);
