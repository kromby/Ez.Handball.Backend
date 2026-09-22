namespace Ez.Handball.Domain;

// Deterministic composite key: a team is identified by its owner + flavor, so the roster /
// budget child tables need no separate id lookup.
public static class GameTeamId
{
    public static string For(string userId, GameFlavor flavor)
        => $"{userId}:{flavor.ToString().ToLowerInvariant()}";

    // Inverse of For. Null when teamId isn't a well-formed id of that flavor (e.g. a bare ":fantasy").
    public static string? UserIdOf(string teamId, GameFlavor flavor)
    {
        var suffix = For(string.Empty, flavor);
        return teamId.Length > suffix.Length && teamId.EndsWith(suffix, StringComparison.Ordinal)
            ? teamId[..^suffix.Length]
            : null;
    }
}
