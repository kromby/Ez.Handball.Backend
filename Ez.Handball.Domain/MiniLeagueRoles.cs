namespace Ez.Handball.Domain;

// Membership roles. Plain strings (no enum) — a small, behavior-free set shared by the
// create use case, #17's join flow, and tests.
public static class MiniLeagueRoles
{
    public const string Creator = "creator";
    public const string Member = "member";
}
