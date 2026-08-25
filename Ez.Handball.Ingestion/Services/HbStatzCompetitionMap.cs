namespace Ez.Handball.Ingestion.Services;

// Maps our own TournamentEntity.CompetitionId to HBStatz's (comp, gender) query params.
// Only entries we've confirmed against the real API are listed — an unmapped competition
// is skipped (logged), not guessed at.
public static class HbStatzCompetitionMap
{
    private static readonly Dictionary<string, (string Comp, string Gender)> Map = new()
    {
        ["olis-karla"] = ("olis", "M"),
    };

    public static bool TryResolve(string competitionId, out string comp, out string gender)
    {
        if (Map.TryGetValue(competitionId, out var value))
        {
            comp = value.Comp;
            gender = value.Gender;
            return true;
        }

        comp = string.Empty;
        gender = string.Empty;
        return false;
    }
}
