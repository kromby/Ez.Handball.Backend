using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.UseCases;

public abstract record RequestPasswordResetResult
{
    // Always Accepted — the endpoint returns 202 regardless, to avoid email enumeration.
    public sealed record Accepted : RequestPasswordResetResult;
}

public interface IRequestPasswordResetUseCase
{
    Task<RequestPasswordResetResult> ExecuteAsync(string email, CancellationToken ct);
}

public sealed class RequestPasswordResetUseCase : IRequestPasswordResetUseCase
{
    private readonly IUserRepository _users;
    private readonly IEmailTokenRepository _emailTokens;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly AuthSettings _settings;

    public RequestPasswordResetUseCase(
        IUserRepository users, IEmailTokenRepository emailTokens, ITokenService tokens,
        IEmailSender email, AuthSettings settings)
    {
        _users = users; _emailTokens = emailTokens; _tokens = tokens; _email = email; _settings = settings;
    }

    public async Task<RequestPasswordResetResult> ExecuteAsync(string email, CancellationToken ct)
    {
        var normalized = AuthValidation.NormalizeEmail(email);
        var user = await _users.GetByEmailAsync(normalized, ct);
        if (user is not null)
        {
            var token = _tokens.CreateEmailToken();
            await _emailTokens.AddAsync(new EmailTokenEntity
            {
                PartitionKey = "reset", RowKey = token.Hash, UserId = user.RowKey, ExpiresAt = token.ExpiresAt
            }, ct);
            var link = _settings.ResetUrlTemplate.Replace("{token}", token.Value);
            await _email.SendPasswordResetEmailAsync(user.Email, link, token.Value, ct);
        }

        return new RequestPasswordResetResult.Accepted();
    }
}
