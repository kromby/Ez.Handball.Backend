namespace Ez.Handball.Domain;

// The fantasy position vocabulary confirmed real by Backend#106 — previously a placeholder
// pending owner review in SeedSquadConstraintsFunction/SeedLineupConstraintsFunction.
// Shared by Ingestion (HBStatz mapping, manual set-position) and Api (admin position fixes).
public static class PositionVocabulary
{
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
    {
        "GK", "LW", "RW", "LB", "CB", "RB", "LP"
    };
}
