using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public interface ILogoutUseCase
{
    /// <summary>Best-effort: revokes one session (from refreshToken) or all of userId's sessions when all=true.</summary>
    Task ExecuteAsync(string? refreshToken, bool all, string? userId, CancellationToken ct);
}

public sealed class LogoutUseCase : ILogoutUseCase
{
    private readonly IRefreshTokenRepository _refresh;
    private readonly ITokenService _tokens;

    public LogoutUseCase(IRefreshTokenRepository refresh, ITokenService tokens)
    {
        _refresh = refresh; _tokens = tokens;
    }

    public async Task ExecuteAsync(string? refreshToken, bool all, string? userId, CancellationToken ct)
    {
        if (all)
        {
            if (!string.IsNullOrEmpty(userId))
                await _refresh.DeleteAllForUserAsync(userId, ct);
            return;
        }

        if (!string.IsNullOrEmpty(refreshToken)
            && _tokens.TryParseRefreshToken(refreshToken, out var uid, out var hash))
        {
            await _refresh.DeleteAsync(uid, hash, ct);
        }
    }
}
