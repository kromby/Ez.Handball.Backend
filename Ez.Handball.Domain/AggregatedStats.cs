namespace Ez.Handball.Domain;

public sealed record AggregatedStats(
    int Games,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards,
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
    double? GradeGoalkeeping = null)
{
    // Recomputed from summed Saves/ShotsFaced rather than averaging each match's
    // percentage — accurate regardless of how unevenly shots faced are spread across games.
    public static double? ComputeSavePct(int saves, int shotsFaced) =>
        shotsFaced > 0 ? Math.Round((double)saves / shotsFaced * 100, 1) : null;

    // Grades are per-match ratings, not counts — averaged over the games that
    // actually carry a grade, not over every game played.
    public static double? AverageGrade(IEnumerable<double?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : Math.Round(present.Average(), 2);
    }
}
