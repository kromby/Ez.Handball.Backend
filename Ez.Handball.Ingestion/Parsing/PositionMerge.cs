using Ez.Handball.Domain;

namespace Ez.Handball.Ingestion.Parsing;

// Backend#167: observation data may correct a player's positions but must never clear one — an
// admin-given secondary the player hasn't played yet would otherwise vanish on the next sync.
// Only known position codes are kept: hsi.is's "Leikmaður" placeholder must never become a
// secondary, since the squad/lineup solver treats a position it has no limit for as unconstrained.
public static class PositionMerge
{
    public static (string Primary, string Secondary) Apply(
        string? existingPrimary, string? existingSecondary, string computedPrimary, string? computedSecondary)
    {
        var secondary = new[] { computedSecondary, existingSecondary, existingPrimary }
            .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)
                && PositionVocabulary.Codes.Contains(code)
                && !string.Equals(code, computedPrimary, StringComparison.Ordinal));
        return (computedPrimary, secondary ?? string.Empty);
    }
}
