using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Mapping;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth-global");

        auth.MapPost("/register", async (
            RegisterCommand cmd, IRegisterUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(cmd, ct);
            return result switch
            {
                RegisterResult.Success s => Results.Json(new
                {
                    accessToken = s.AccessToken, refreshToken = s.RefreshToken,
                    expiresIn = s.ExpiresIn, user = s.User.ToProfile()
                }, statusCode: StatusCodes.Status201Created),
                RegisterResult.EmailTaken       => Results.Conflict(new { error = "email_taken" }),
                RegisterResult.InvalidClub      => Results.BadRequest(new { error = "invalid_club" }),
                RegisterResult.WeakPassword     => Results.BadRequest(new { error = "weak_password" }),
                RegisterResult.ValidationError v => Results.BadRequest(new { error = "validation_error", details = new { field = v.Field } }),
                _                               => Results.Problem()
            };
        });

        auth.MapPost("/login", async (
            LoginCommand cmd, ILoginUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(cmd, ct);
            return result switch
            {
                LoginResult.Success s => Results.Ok(new
                {
                    accessToken = s.AccessToken, refreshToken = s.RefreshToken,
                    expiresIn = s.ExpiresIn, user = s.User.ToProfile()
                }),
                LoginResult.InvalidCredentials => Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized),
                _                              => Results.Problem()
            };
        }).RequireRateLimiting("auth-sensitive");

        auth.MapPost("/refresh", async (
            RefreshRequest req, IRefreshTokenUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(req.RefreshToken ?? string.Empty, ct);
            return result switch
            {
                RefreshTokenResult.Success s => Results.Ok(new
                {
                    accessToken = s.AccessToken, refreshToken = s.RefreshToken,
                    expiresIn = s.ExpiresIn, user = s.User.ToProfile()
                }),
                RefreshTokenResult.TokenExpired => Results.Json(new { error = "token_expired" }, statusCode: StatusCodes.Status401Unauthorized),
                RefreshTokenResult.InvalidToken => Results.Json(new { error = "invalid_token" }, statusCode: StatusCodes.Status401Unauthorized),
                _                               => Results.Problem()
            };
        });

        auth.MapPost("/logout", async (
            LogoutRequest req, bool? all, HttpContext http, ILogoutUseCase uc, CancellationToken ct) =>
        {
            var revokeAll = all == true;
            var userId = http.User.UserId();
            if (revokeAll && string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            await uc.ExecuteAsync(req.RefreshToken, revokeAll, userId, ct);
            return Results.NoContent();
        });

        auth.MapPost("/verify", async (
            VerifyRequest req, IVerifyEmailUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(req.Token ?? string.Empty, ct);
            return result switch
            {
                VerifyEmailResult.Success      => Results.Ok(new { verified = true }),
                VerifyEmailResult.TokenExpired => Results.Json(new { error = "token_expired" }, statusCode: StatusCodes.Status401Unauthorized),
                VerifyEmailResult.InvalidToken => Results.Json(new { error = "invalid_token" }, statusCode: StatusCodes.Status401Unauthorized),
                _                              => Results.Problem()
            };
        });

        auth.MapPost("/resend-verification", async (
            HttpContext http, IResendVerificationUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, ct);
            return result switch
            {
                ResendVerificationResult.Accepted => Results.Accepted(),
                ResendVerificationResult.NotFound => Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized),
                _                                 => Results.Problem()
            };
        }).RequireAuthorization();

        auth.MapPost("/password/reset-request", async (
            ResetRequestRequest req, IRequestPasswordResetUseCase uc, CancellationToken ct) =>
        {
            await uc.ExecuteAsync(req.Email ?? string.Empty, ct);
            return Results.Accepted();   // always 202 — no enumeration
        }).RequireRateLimiting("auth-sensitive");

        auth.MapPost("/password/reset", async (
            ResetPasswordCommand cmd, IResetPasswordUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(cmd, ct);
            return result switch
            {
                ResetPasswordResult.Success      => Results.Ok(new { reset = true }),
                ResetPasswordResult.WeakPassword => Results.BadRequest(new { error = "weak_password" }),
                ResetPasswordResult.TokenExpired => Results.Json(new { error = "token_expired" }, statusCode: StatusCodes.Status401Unauthorized),
                ResetPasswordResult.InvalidToken => Results.Json(new { error = "invalid_token" }, statusCode: StatusCodes.Status401Unauthorized),
                _                                => Results.Problem()
            };
        });

        var users = app.MapGroup("/api/users");

        users.MapGet("/me", async (
            HttpContext http, IUserRepository repo, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var user = await repo.GetByIdAsync(userId, ct);
            return user is null
                ? Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(user.ToProfile());
        }).RequireAuthorization();

        users.MapPatch("/me", async (
            UpdateProfileCommand cmd, HttpContext http, IUpdateProfileUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, cmd, ct);
            return result switch
            {
                UpdateProfileResult.Success s    => Results.Ok(s.User.ToProfile()),
                UpdateProfileResult.InvalidClub  => Results.BadRequest(new { error = "invalid_club" }),
                UpdateProfileResult.ValidationError v => Results.BadRequest(new { error = "validation_error", details = new { field = v.Field } }),
                UpdateProfileResult.NotFound     => Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized),
                _                                => Results.Problem()
            };
        }).RequireAuthorization();
    }

    // Request DTOs for endpoints whose body is a single field (kept out of the Application layer).
    public sealed record RefreshRequest(string? RefreshToken);
    public sealed record LogoutRequest(string? RefreshToken);
    public sealed record VerifyRequest(string? Token);
    public sealed record ResetRequestRequest(string? Email);
}
