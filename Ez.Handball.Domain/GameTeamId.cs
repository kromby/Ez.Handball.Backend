namespace Ez.Handball.Domain;

// Deterministic composite key: a team is identified by its owner + flavor, so the roster /
// budget child tables need no separate id lookup.
public static class GameTeamId
{
    public static string For(string userId, GameFlavor flavor)
        => $"{userId}:{flavor.ToString().ToLowerInvariant()}";
}
