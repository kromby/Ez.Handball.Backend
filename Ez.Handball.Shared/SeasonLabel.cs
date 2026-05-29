namespace Ez.Handball.Shared;

/// <summary>
/// Formats a season's start year into the Icelandic "YYYY-YY" label
/// (e.g. 2025 -> "2025-26"). This label is the canonical season value:
/// SeedTournamentsFunction stores it as the Tournaments PartitionKey, and
/// it is denormalized onto PlayerStatEntity.Season at parse time.
/// </summary>
public static class SeasonLabel
{
    public static string Format(int startYear)
        => $"{startYear}-{(startYear + 1) % 100:D2}";

    /// <summary>
    /// Resolves a season query parameter (an integer start year) into a label.
    /// Falls back to <paramref name="fallbackYear"/> when the param is missing
    /// or not an integer.
    /// </summary>
    public static string Resolve(string? seasonParam, int fallbackYear)
    {
        var startYear = int.TryParse(seasonParam, out var parsed) ? parsed : fallbackYear;
        return Format(startYear);
    }
}
