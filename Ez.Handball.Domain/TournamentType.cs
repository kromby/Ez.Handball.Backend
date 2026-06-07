namespace Ez.Handball.Domain;

public enum TournamentType
{
    League,
    Playoffs,
    Cup
}

/// <summary>
/// Single source of truth for the canonical lowercase wire form of
/// <see cref="TournamentType"/>. Used by the API edge (query parsing), the
/// response serializer, and the Tournaments repository (stored-string mapping).
/// </summary>
public static class TournamentTypes
{
    public static string ToWireString(this TournamentType type) => type switch
    {
        TournamentType.League => "league",
        TournamentType.Playoffs => "playoffs",
        TournamentType.Cup => "cup",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static bool TryParse(string? value, out TournamentType type)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "league": type = TournamentType.League; return true;
            case "playoffs": type = TournamentType.Playoffs; return true;
            case "cup": type = TournamentType.Cup; return true;
            default: type = default; return false;
        }
    }
}
