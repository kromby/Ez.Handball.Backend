namespace Ez.Handball.Domain;

// A user's team header for one flavour, as read by the manager use cases.
public sealed record GameTeam(
    string TeamId,
    string Name,
    string Color,
    DateTimeOffset CreatedAt);
