namespace Ez.Handball.Domain;

public sealed record UserProfile(
    string Id,
    string Email,
    string DisplayName,
    string Language,
    string FavoriteClubId,
    bool EmailVerified,
    bool IsAdmin,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    string? TeamName = null);
