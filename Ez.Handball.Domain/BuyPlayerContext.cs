namespace Ez.Handball.Domain;

public sealed record BuyPlayerContext(
    string? Season,            // null/blank => resolve current season
    string? TournamentId,      // optional narrower scope
    int?    RuleSetVersion);   // null => the flavor's default pricing version
