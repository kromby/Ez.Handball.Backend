namespace Ez.Handball.Domain;

// One entry in the priced player list. Carries real-life stats AND fantasy
// rating/price so a single table can sort by either. PickPercentage is
// reserved — always null until the ownership aggregation follow-up ships.
public record PlayerPoolEntry(
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
    double? PickPercentage,
    string? PositionSecondary = null,
    // HBStatz-derived season aggregates — null/0 for players HBStatz hasn't observed yet.
    int Assists = 0,
    int Steals = 0,
    int Blocks = 0,
    int Saves = 0,
    int Turnovers = 0,
    int LegalStops = 0,
    int Shots = 0,
    double ExpectedGoals = 0,
    int ShotsFaced = 0,
    double? SavePct = null,
    double ExpectedSaves = 0,
    double? GradeTotal = null,
    double? GradeOffense = null,
    double? GradeDefense = null,
    double? GradeGoalkeeping = null);

public sealed record PlayerPool(
    string Sort,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<PlayerPoolEntry> Entries);
