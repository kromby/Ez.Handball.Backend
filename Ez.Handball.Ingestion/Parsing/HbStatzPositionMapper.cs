namespace Ez.Handball.Ingestion.Parsing;

// Maps HBStatz's English position labels (hbstatz.is/api/game.php's "position" field) onto the
// fantasy vocabulary. Unrecognized labels return null rather than throwing, so an HBStatz label
// we haven't seen yet is silently skipped instead of breaking the sync.
public static class HbStatzPositionMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Goalkeeper"] = "GK",
            ["Left Wing"] = "LW",
            ["Right Wing"] = "RW",
            ["Left Back"] = "LB",
            ["Right Back"] = "RB",
            ["Center"] = "CB",
            ["Line"] = "LP",
        };

    public static string? MapToCode(string? hbStatzPosition)
    {
        if (string.IsNullOrWhiteSpace(hbStatzPosition)) return null;
        return Map.TryGetValue(hbStatzPosition.Trim(), out var code) ? code : null;
    }
}
