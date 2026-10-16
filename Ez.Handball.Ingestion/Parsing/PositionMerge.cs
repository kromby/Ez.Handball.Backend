namespace Ez.Handball.Ingestion.Parsing;

// Backend#167: observation data may correct a player's positions but must never clear one — an
// admin-given secondary the player hasn't played yet would otherwise vanish on the next sync.
public static class PositionMerge
{
    public static (string Primary, string Secondary) Apply(
        string? existingPrimary, string? existingSecondary, string computedPrimary, string? computedSecondary)
    {
        var secondary = new[] { computedSecondary, existingSecondary, existingPrimary }
            .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)
                && !string.Equals(code, computedPrimary, StringComparison.Ordinal));
        return (computedPrimary, secondary ?? string.Empty);
    }
}
