using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.UseCases;

public abstract record ResendVerificationResult
{
    public sealed record Accepted : ResendVerificationResult;
    public sealed record NotFound : ResendVerificationResult;
}

public interface IResendVerificationUseCase
{
    Task<ResendVerificationResult> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class ResendVerificationUseCase : IResendVerificationUseCase
{
    private readonly IUserRepository _users;
    private readonly IEmailTokenRepository _emailTokens;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly AuthSettings _settings;

    public ResendVerificationUseCase(
        IUserRepository users, IEmailTokenRepository emailTokens, ITokenService tokens,
        IEmailSender email, AuthSettings settings)
    {
        _users = users; _emailTokens = emailTokens; _tokens = tokens; _email = email; _settings = settings;
    }

    public async Task<ResendVerificationResult> ExecuteAsync(string userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return new ResendVerificationResult.NotFound();
        if (user.EmailVerified) return new ResendVerificationResult.Accepted();

        var token = _tokens.CreateEmailToken();
        await _emailTokens.AddAsync(new EmailTokenEntity
        {
            PartitionKey = "verify", RowKey = token.Hash, UserId = userId, ExpiresAt = token.ExpiresAt
        }, ct);
        var link = _settings.VerificationUrlTemplate.Replace("{token}", token.Value);
        await _email.SendVerificationEmailAsync(user.Email, link, token.Value, ct);

        return new ResendVerificationResult.Accepted();
    }
}
