using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.Mapping;

public static class UserMapping
{
    // Single projection point — guarantees PasswordHash never crosses the API boundary.
    public static UserProfile ToProfile(this UserEntity e) => new(
        Id: e.RowKey,
        Email: e.Email,
        DisplayName: e.DisplayName,
        Language: e.Language,
        FavoriteClubId: e.FavoriteClubId,
        EmailVerified: e.EmailVerified,
        CreatedAt: e.CreatedAt,
        LastLoginAt: e.LastLoginAt);
}
