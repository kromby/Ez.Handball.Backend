namespace Ez.Handball.Domain;

public sealed record UserProfile(
    string Id,
    string Email,
    string DisplayName,
    string Language,
    string FavoriteClubId,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
