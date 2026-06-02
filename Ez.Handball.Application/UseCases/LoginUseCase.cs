using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.UseCases;

public sealed record LoginCommand(string Email, string Password);

public abstract record LoginResult
{
    public sealed record Success(string AccessToken, string RefreshToken, int ExpiresIn, UserEntity User) : LoginResult;
    public sealed record InvalidCredentials : LoginResult;
}

public interface ILoginUseCase
{
    Task<LoginResult> ExecuteAsync(LoginCommand cmd, CancellationToken ct);
}

public sealed class LoginUseCase : ILoginUseCase
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refresh;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly Func<DateTimeOffset> _now;

    public LoginUseCase(
        IUserRepository users, IRefreshTokenRepository refresh,
        IPasswordHasher hasher, ITokenService tokens, Func<DateTimeOffset> now)
    {
        _users = users; _refresh = refresh; _hasher = hasher; _tokens = tokens; _now = now;
    }

    public async Task<LoginResult> ExecuteAsync(LoginCommand cmd, CancellationToken ct)
    {
        var email = AuthValidation.NormalizeEmail(cmd.Email);
        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null)
        {
            _hasher.VerifyDummy(cmd.Password); // equalize timing vs the real verify path (no enumeration)
            return new LoginResult.InvalidCredentials();
        }
        if (!_hasher.Verify(cmd.Password, user.PasswordHash)) return new LoginResult.InvalidCredentials();

        var now = _now();
        user.LastLoginAt = now;
        await _users.UpdateAsync(user, ct);

        var access = _tokens.CreateAccessToken(user);
        var refresh = _tokens.CreateRefreshToken(user.RowKey);
        await _refresh.AddAsync(new RefreshTokenEntity
        {
            PartitionKey = user.RowKey, RowKey = refresh.Hash, ExpiresAt = refresh.ExpiresAt, CreatedAt = now
        }, ct);

        return new LoginResult.Success(access, refresh.Value, _tokens.AccessTokenSeconds, user);
    }
}
