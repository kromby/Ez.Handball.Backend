namespace Ez.Handball.Domain;

public sealed record Player(
    string PlayerId,
    string Name,
    string? JerseyNumber,
    DateOnly? DateOfBirth,
    int? Age,
    string TeamId,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    bool Retired);
