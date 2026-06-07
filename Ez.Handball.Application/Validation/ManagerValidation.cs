namespace Ez.Handball.Application.Validation;

public static class ManagerValidation
{
    // Reserved words (exact match) and profanity (substring match), all normalized lowercase.
    // Minimal starter set — extend as needed. Substrings catch embedded profanity; reserved
    // words guard impersonation of system/role names.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "moderator", "support", "system", "root", "official",
    };

    private static readonly string[] Profanity =
    {
        "fuck", "shit", "cunt", "nigger", "faggot",
    };

    public static string NormalizeTeamName(string name)
        => (name ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsAllowedTeamName(string name)
    {
        var normalized = NormalizeTeamName(name);
        if (Reserved.Contains(normalized)) return false;
        foreach (var term in Profanity)
            if (normalized.Contains(term, StringComparison.Ordinal)) return false;
        return true;
    }
}
