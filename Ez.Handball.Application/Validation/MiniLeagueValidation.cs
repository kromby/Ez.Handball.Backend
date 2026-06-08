namespace Ez.Handball.Application.Validation;

public static class MiniLeagueValidation
{
    // 1–60 characters after trimming, non-empty. Mirrors AuthValidation.IsValidTeamName.
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Trim().Length is >= 1 and <= 60;
    }
}
