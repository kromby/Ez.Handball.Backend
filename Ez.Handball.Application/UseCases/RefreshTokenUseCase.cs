using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.UseCases;

public abstract record RefreshTokenResult
{
    public sealed record Success(string AccessToken, string RefreshToken, int ExpiresIn, UserEntity User) : RefreshTokenResult;
    public sealed record InvalidToken : RefreshTokenResult;
    public sealed record TokenExpired : RefreshTokenResult;
}

public interface IRefreshTokenUseCase
{
    Task<RefreshTokenResult> ExecuteAsync(string presentedRefreshToken, CancellationToken ct);
}

public sealed class RefreshTokenUseCase : IRefreshTokenUseCase
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refresh;
    private readonly ITokenService _tokens;
    private readonly Func<DateTimeOffset> _now;

    public RefreshTokenUseCase(
        IUserRepository users, IRefreshTokenRepository refresh, ITokenService tokens, Func<DateTimeOffset> now)
    {
        _users = users; _refresh = refresh; _tokens = tokens; _now = now;
    }

    public async Task<RefreshTokenResult> ExecuteAsync(string presentedRefreshToken, CancellationToken ct)
    {
        if (!_tokens.TryParseRefreshToken(presentedRefreshToken, out var userId, out var hash))
            return new RefreshTokenResult.InvalidToken();

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return new RefreshTokenResult.InvalidToken();

        var existing = await _refresh.GetAsync(userId, hash, ct);
        if (existing is null)
        {
            // Well-formed token for a real user, but its hash is gone → replay of a rotated token.
            await _refresh.DeleteAllForUserAsync(userId, ct);
            return new RefreshTokenResult.InvalidToken();
        }

        if (existing.ExpiresAt <= _now())
        {
            await _refresh.DeleteAsync(userId, hash, ct);
            return new RefreshTokenResult.TokenExpired();
        }

        // Rotate: delete the presented row, insert a fresh one.
        await _refresh.DeleteAsync(userId, hash, ct);
        var now = _now();
        var refresh = _tokens.CreateRefreshToken(userId);
        await _refresh.AddAsync(new RefreshTokenEntity
        {
            PartitionKey = userId, RowKey = refresh.Hash, ExpiresAt = refresh.ExpiresAt, CreatedAt = now
        }, ct);

        var access = _tokens.CreateAccessToken(user);
        return new RefreshTokenResult.Success(access, refresh.Value, _tokens.AccessTokenSeconds, user);
    }
}
