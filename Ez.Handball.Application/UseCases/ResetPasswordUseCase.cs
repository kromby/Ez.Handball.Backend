using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;

namespace Ez.Handball.Application.UseCases;

public sealed record ResetPasswordCommand(string Token, string NewPassword);

public abstract record ResetPasswordResult
{
    public sealed record Success : ResetPasswordResult;
    public sealed record WeakPassword : ResetPasswordResult;
    public sealed record InvalidToken : ResetPasswordResult;
    public sealed record TokenExpired : ResetPasswordResult;
}

public interface IResetPasswordUseCase
{
    Task<ResetPasswordResult> ExecuteAsync(ResetPasswordCommand cmd, CancellationToken ct);
}

public sealed class ResetPasswordUseCase : IResetPasswordUseCase
{
    private readonly IUserRepository _users;
    private readonly IEmailTokenRepository _emailTokens;
    private readonly IRefreshTokenRepository _refresh;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly Func<DateTimeOffset> _now;

    public ResetPasswordUseCase(
        IUserRepository users, IEmailTokenRepository emailTokens, IRefreshTokenRepository refresh,
        IPasswordHasher hasher, ITokenService tokens, Func<DateTimeOffset> now)
    {
        _users = users; _emailTokens = emailTokens; _refresh = refresh;
        _hasher = hasher; _tokens = tokens; _now = now;
    }

    public async Task<ResetPasswordResult> ExecuteAsync(ResetPasswordCommand cmd, CancellationToken ct)
    {
        if (!AuthValidation.IsValidPassword(cmd.NewPassword)) return new ResetPasswordResult.WeakPassword();
        if (!_tokens.TryHashEmailToken(cmd.Token, out var hash)) return new ResetPasswordResult.InvalidToken();

        var row = await _emailTokens.GetAsync("reset", hash, ct);
        if (row is null) return new ResetPasswordResult.InvalidToken();

        if (row.ExpiresAt <= _now())
        {
            await _emailTokens.DeleteAsync("reset", hash, ct);
            return new ResetPasswordResult.TokenExpired();
        }

        var user = await _users.GetByIdAsync(row.UserId, ct);
        if (user is null) return new ResetPasswordResult.InvalidToken();

        user.PasswordHash = _hasher.Hash(cmd.NewPassword);
        user.ChangedAt = _now();
        await _users.UpdateAsync(user, ct);
        await _emailTokens.DeleteAsync("reset", hash, ct);
        await _refresh.DeleteAllForUserAsync(user.RowKey, ct);   // sign out everywhere on reset

        return new ResetPasswordResult.Success();
    }
}
