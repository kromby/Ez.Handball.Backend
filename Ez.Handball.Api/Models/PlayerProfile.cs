namespace Ez.Handball.Api.Models;

public record PlayerProfile(
    string PlayerId,
    string Name,
    string? JerseyNumber,
    DateTimeOffset? DateOfBirth,
    int? Age,
    string TeamId,
    string ClubId,
    string? ClubName,
    string Gender);
