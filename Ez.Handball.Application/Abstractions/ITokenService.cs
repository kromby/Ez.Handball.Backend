using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.Abstractions;

/// <summary>An opaque secret handed to the client plus the hash stored server-side.</summary>
public sealed record IssuedToken(string Value, string Hash, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    string CreateAccessToken(UserEntity user);
    int AccessTokenSeconds { get; }

    IssuedToken CreateRefreshToken(string userId);
    /// <summary>Parses "base64url(userId).base64url(secret)" and returns userId + hex SHA-256(secret).</summary>
    bool TryParseRefreshToken(string presented, out string userId, out string secretHash);

    IssuedToken CreateEmailToken();
    /// <summary>Hashes a presented email/reset token secret for lookup. False if malformed.</summary>
    bool TryHashEmailToken(string presented, out string tokenHash);

    /// <summary>An opaque, URL-safe invite code. Stored plaintext (not hashed) — it is the lookup key and is re-displayed.</summary>
    string CreateInviteCode();
}
