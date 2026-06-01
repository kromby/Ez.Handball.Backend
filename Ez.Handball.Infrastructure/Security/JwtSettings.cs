namespace Ez.Handball.Infrastructure.Security;

public sealed record JwtSettings(
    string SigningKey,
    string Issuer,
    string Audience,
    int AccessTokenMinutes,
    int RefreshTokenDays,
    int EmailTokenHours);
