namespace Ez.Handball.Application.Validation;

public static class AuthValidation
{
    public static string NormalizeEmail(string email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@')) return false;
        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
    }

    public static bool IsValidPassword(string password)
        => !string.IsNullOrEmpty(password) && password.Length >= 8 && password.Length <= 128;

    public static bool IsValidDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;
        return displayName.Trim().Length is >= 1 and <= 60;
    }

    public static bool IsValidLanguage(string language)
        => language is "is" or "en";

    public static bool IsValidTeamName(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return false;
        return teamName.Trim().Length is >= 1 and <= 60;
    }
}
