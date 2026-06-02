using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Ez.Handball.Infrastructure.Security;

internal sealed class JwtTokenService : ITokenService
{
    private const int SecretBytes = 32; // 256 bits

    /// <summary>
    /// Stable key id stamped onto issued tokens so the JsonWebTokenHandler used by the
    /// JwtBearer middleware can resolve the signing key (avoids IDX10517 "kid is missing").
    /// Must match the KeyId set on the validating IssuerSigningKey in Program.cs.
    /// </summary>
    public const string SigningKeyId = "ezhb-hs256";

    private readonly JwtSettings _settings;
    private readonly Func<DateTimeOffset> _now;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(JwtSettings settings, Func<DateTimeOffset> now)
    {
        _settings = settings;
        _now = now;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)) { KeyId = SigningKeyId };
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public int AccessTokenSeconds => _settings.AccessTokenMinutes * 60;

    public string CreateAccessToken(UserEntity user)
    {
        var now = _now();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.RowKey),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("email_verified", user.EmailVerified ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.UtcDateTime.AddMinutes(_settings.AccessTokenMinutes),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public IssuedToken CreateRefreshToken(string userId)
    {
        var secret = RandomNumberGenerator.GetBytes(SecretBytes);
        var value = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(userId)) + "." + Base64UrlEncoder.Encode(secret);
        return new IssuedToken(value, HashBytes(secret), _now().AddDays(_settings.RefreshTokenDays));
    }

    public bool TryParseRefreshToken(string presented, out string userId, out string secretHash)
    {
        userId = string.Empty;
        secretHash = string.Empty;
        if (string.IsNullOrWhiteSpace(presented)) return false;

        var parts = presented.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            userId = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[0]));
            var secret = Base64UrlEncoder.DecodeBytes(parts[1]);
            if (userId.Length == 0 || secret.Length == 0) return false;
            secretHash = HashBytes(secret);
            return true;
        }
        catch (FormatException) { return false; }
    }

    public IssuedToken CreateEmailToken()
    {
        var secret = RandomNumberGenerator.GetBytes(SecretBytes);
        var value = Base64UrlEncoder.Encode(secret);
        return new IssuedToken(value, HashBytes(secret), _now().AddHours(_settings.EmailTokenHours));
    }

    public bool TryHashEmailToken(string presented, out string tokenHash)
    {
        tokenHash = string.Empty;
        if (string.IsNullOrWhiteSpace(presented)) return false;
        try
        {
            var secret = Base64UrlEncoder.DecodeBytes(presented);
            if (secret.Length == 0) return false;
            tokenHash = HashBytes(secret);
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static string HashBytes(byte[] secret) =>
        Convert.ToHexString(SHA256.HashData(secret)).ToLowerInvariant();
}
