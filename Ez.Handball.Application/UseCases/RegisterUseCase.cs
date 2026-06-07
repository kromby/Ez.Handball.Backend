using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.UseCases;

public sealed record RegisterCommand(
    string Email, string Password, string DisplayName, string Language, string FavoriteClubId, string TeamName);

public abstract record RegisterResult
{
    public sealed record Success(string AccessToken, string RefreshToken, int ExpiresIn, UserEntity User) : RegisterResult;
    public sealed record EmailTaken : RegisterResult;
    public sealed record InvalidClub : RegisterResult;
    public sealed record WeakPassword : RegisterResult;
    public sealed record ValidationError(string Field) : RegisterResult;
}

public interface IRegisterUseCase
{
    Task<RegisterResult> ExecuteAsync(RegisterCommand cmd, CancellationToken ct);
}

public sealed class RegisterUseCase : IRegisterUseCase
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refresh;
    private readonly IEmailTokenRepository _emailTokens;
    private readonly IClubRepository _clubs;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly AuthSettings _settings;
    private readonly Func<DateTimeOffset> _now;
    private readonly ITeamProvisioningService _provisioning;

    public RegisterUseCase(
        IUserRepository users, IRefreshTokenRepository refresh, IEmailTokenRepository emailTokens,
        IClubRepository clubs, IPasswordHasher hasher, ITokenService tokens, IEmailSender email,
        AuthSettings settings, Func<DateTimeOffset> now, ITeamProvisioningService provisioning)
    {
        _users = users; _refresh = refresh; _emailTokens = emailTokens; _clubs = clubs;
        _hasher = hasher; _tokens = tokens; _email = email; _settings = settings; _now = now;
        _provisioning = provisioning;
    }

    public async Task<RegisterResult> ExecuteAsync(RegisterCommand cmd, CancellationToken ct)
    {
        var email = AuthValidation.NormalizeEmail(cmd.Email);
        if (!AuthValidation.IsValidEmail(email)) return new RegisterResult.ValidationError("email");
        if (!AuthValidation.IsValidDisplayName(cmd.DisplayName)) return new RegisterResult.ValidationError("displayName");
        if (!AuthValidation.IsValidLanguage(cmd.Language)) return new RegisterResult.ValidationError("language");
        if (!AuthValidation.IsValidTeamName(cmd.TeamName)) return new RegisterResult.ValidationError("teamName");
        if (!AuthValidation.IsValidPassword(cmd.Password)) return new RegisterResult.WeakPassword();
        if (!await _clubs.ExistsAsync(cmd.FavoriteClubId, ct)) return new RegisterResult.InvalidClub();

        var userId = Guid.NewGuid().ToString("N");
        if (!await _users.TryReserveEmailAsync(email, userId, ct)) return new RegisterResult.EmailTaken();

        var now = _now();
        var user = new UserEntity
        {
            RowKey = userId,
            Email = email,
            DisplayName = cmd.DisplayName.Trim(),
            Language = cmd.Language,
            FavoriteClubId = cmd.FavoriteClubId,
            EmailVerified = false,
            PasswordHash = _hasher.Hash(cmd.Password),
            CreatedAt = now,
            ChangedAt = now
        };
        await _users.AddAsync(user, ct);
        await _provisioning.ProvisionAsync(userId, GameFlavor.Fantasy, cmd.TeamName.Trim(), ManagerColor.ForClub(cmd.FavoriteClubId), ct);

        var emailToken = _tokens.CreateEmailToken();
        await _emailTokens.AddAsync(new EmailTokenEntity
        {
            PartitionKey = "verify", RowKey = emailToken.Hash, UserId = userId, ExpiresAt = emailToken.ExpiresAt
        }, ct);
        var link = _settings.VerificationUrlTemplate.Replace("{token}", emailToken.Value);
        await _email.SendVerificationEmailAsync(email, link, emailToken.Value, ct);

        var access = _tokens.CreateAccessToken(user);
        var refresh = _tokens.CreateRefreshToken(userId);
        await _refresh.AddAsync(new RefreshTokenEntity
        {
            PartitionKey = userId, RowKey = refresh.Hash, ExpiresAt = refresh.ExpiresAt, CreatedAt = now
        }, ct);

        return new RegisterResult.Success(access, refresh.Value, _tokens.AccessTokenSeconds, user);
    }
}
