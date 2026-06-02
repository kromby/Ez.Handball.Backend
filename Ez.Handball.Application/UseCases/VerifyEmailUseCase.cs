using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public abstract record VerifyEmailResult
{
    public sealed record Success : VerifyEmailResult;
    public sealed record InvalidToken : VerifyEmailResult;
    public sealed record TokenExpired : VerifyEmailResult;
}

public interface IVerifyEmailUseCase
{
    Task<VerifyEmailResult> ExecuteAsync(string token, CancellationToken ct);
}

public sealed class VerifyEmailUseCase : IVerifyEmailUseCase
{
    private readonly IUserRepository _users;
    private readonly IEmailTokenRepository _emailTokens;
    private readonly ITokenService _tokens;
    private readonly Func<DateTimeOffset> _now;

    public VerifyEmailUseCase(
        IUserRepository users, IEmailTokenRepository emailTokens, ITokenService tokens, Func<DateTimeOffset> now)
    {
        _users = users; _emailTokens = emailTokens; _tokens = tokens; _now = now;
    }

    public async Task<VerifyEmailResult> ExecuteAsync(string token, CancellationToken ct)
    {
        if (!_tokens.TryHashEmailToken(token, out var hash)) return new VerifyEmailResult.InvalidToken();

        var row = await _emailTokens.GetAsync("verify", hash, ct);
        if (row is null) return new VerifyEmailResult.InvalidToken();

        if (row.ExpiresAt <= _now())
        {
            await _emailTokens.DeleteAsync("verify", hash, ct);
            return new VerifyEmailResult.TokenExpired();
        }

        var user = await _users.GetByIdAsync(row.UserId, ct);
        if (user is null) return new VerifyEmailResult.InvalidToken();

        user.EmailVerified = true;
        user.ChangedAt = _now();
        await _users.UpdateAsync(user, ct);
        await _emailTokens.DeleteAsync("verify", hash, ct);

        return new VerifyEmailResult.Success();
    }
}
