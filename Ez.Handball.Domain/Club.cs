namespace Ez.Handball.Domain;

public sealed record Club(
    string ClubId,
    string Name,
    string? LogoUrl);
