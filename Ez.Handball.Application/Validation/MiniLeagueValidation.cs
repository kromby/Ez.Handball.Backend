namespace Ez.Handball.Application.Validation;

public static class MiniLeagueValidation
{
    // HSÍ round labels are short ("6", "Undanúrslit"); anything longer is not a round.
    private const int MaxStartRoundLength = 40;

    // 1–60 characters after trimming, non-empty. Mirrors AuthValidation.IsValidTeamName.
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Trim().Length is >= 1 and <= 60;
    }

    // Null/blank means "count every round" and normalizes to null; otherwise the trimmed label.
    // False only for a label that is too long to be a round.
    public static bool TryNormalizeStartRound(string? startRound, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(startRound) ? null : startRound.Trim();
        return normalized is null || normalized.Length <= MaxStartRoundLength;
    }
}
