namespace Ez.Handball.Domain;

// A raw roster row (currency-less; currency is applied from constraints at a higher layer).
// DeletedAt is null while the player is owned.
public sealed record RosterEntry(
    string PlayerId,
    string? Position,
    double PricePaidAmount,
    DateTimeOffset? DeletedAt);
