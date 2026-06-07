namespace Ez.Handball.Domain;

public sealed record PlayerRatingComponent(
    string Key,
    double Value,
    double Weight,
    double Contribution);

public sealed record PlayerRating(
    string PlayerId,
    string Flavor,   // lowercased flavor name, e.g. "fantasy"
    double Rating,
    IReadOnlyList<PlayerRatingComponent> Components,
    string Version);
