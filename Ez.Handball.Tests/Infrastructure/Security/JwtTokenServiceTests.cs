using System.IdentityModel.Tokens.Jwt;
using Ez.Handball.Infrastructure.Security;
using Ez.Handball.Shared.Entities;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Ez.Handball.Tests.Infrastructure.Security;

public class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtSettings Settings = new(
        SigningKey: "test-signing-key-at-least-32-bytes-long!!",
        Issuer: "ez-handball-test",
        Audience: "ez-handball-web-test",
        AccessTokenMinutes: 15,
        RefreshTokenDays: 30,
        EmailTokenHours: 24);

    private static JwtTokenService CreateSut() => new(Settings, () => Now);

    private static UserEntity User() => new()
    {
        RowKey = "u-123", Email = "a@b.is", DisplayName = "Jón", EmailVerified = true
    };

    [Fact]
    public void CreateAccessToken_EmbedsExpectedClaimsAndIsValidlySigned()
    {
        var jwt = CreateSut().CreateAccessToken(User());

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(jwt, new TokenValidationParameters
        {
            ValidIssuer = Settings.Issuer,
            ValidAudience = Settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.SigningKey)),
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        }, out _);

        Assert.Equal("u-123", principal.FindFirst("sub")!.Value);
        Assert.Equal("a@b.is", principal.FindFirst("email")!.Value);
        Assert.Equal("true", principal.FindFirst("email_verified")!.Value);
        Assert.Equal("Jón", principal.FindFirst("name")!.Value);
    }

    [Fact]
    public void AccessTokenSeconds_IsMinutesTimesSixty() =>
        Assert.Equal(15 * 60, CreateSut().AccessTokenSeconds);

    [Fact]
    public void CreateRefreshToken_RoundTripsThroughParse()
    {
        var sut = CreateSut();
        var issued = sut.CreateRefreshToken("u-123");

        Assert.Equal(Now.AddDays(30), issued.ExpiresAt);
        Assert.True(sut.TryParseRefreshToken(issued.Value, out var userId, out var hash));
        Assert.Equal("u-123", userId);
        Assert.Equal(issued.Hash, hash);
    }

    [Fact]
    public void TryParseRefreshToken_Malformed_ReturnsFalse()
    {
        Assert.False(CreateSut().TryParseRefreshToken("not-a-valid-token", out _, out _));
    }

    [Fact]
    public void CreateEmailToken_HashesBackViaTryHash()
    {
        var sut = CreateSut();
        var issued = sut.CreateEmailToken();

        Assert.Equal(Now.AddHours(24), issued.ExpiresAt);
        Assert.True(sut.TryHashEmailToken(issued.Value, out var hash));
        Assert.Equal(issued.Hash, hash);
    }

    [Fact]
    public void TryHashEmailToken_Malformed_ReturnsFalse()
    {
        Assert.False(CreateSut().TryHashEmailToken("!!!not-base64url!!!", out _));
    }

    [Fact]
    public void Constructor_ShortSigningKey_Throws()
    {
        var bad = Settings with { SigningKey = "too-short" };
        Assert.Throws<ArgumentException>(() => new JwtTokenService(bad, () => Now));
    }

    [Fact]
    public void StoredHashesAreRowKeySafeHex()
    {
        var issued = CreateSut().CreateRefreshToken("u-123");
        Assert.Matches("^[0-9a-f]{64}$", issued.Hash);
    }
}
