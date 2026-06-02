using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.UseCases;

public sealed record UpdateProfileCommand(string? DisplayName, string? Language, string? FavoriteClubId);

public abstract record UpdateProfileResult
{
    public sealed record Success(UserEntity User) : UpdateProfileResult;
    public sealed record InvalidClub : UpdateProfileResult;
    public sealed record ValidationError(string Field) : UpdateProfileResult;
    public sealed record NotFound : UpdateProfileResult;
}

public interface IUpdateProfileUseCase
{
    Task<UpdateProfileResult> ExecuteAsync(string userId, UpdateProfileCommand cmd, CancellationToken ct);
}

public sealed class UpdateProfileUseCase : IUpdateProfileUseCase
{
    private readonly IUserRepository _users;
    private readonly IClubRepository _clubs;
    private readonly Func<DateTimeOffset> _now;

    public UpdateProfileUseCase(IUserRepository users, IClubRepository clubs, Func<DateTimeOffset> now)
    {
        _users = users; _clubs = clubs; _now = now;
    }

    public async Task<UpdateProfileResult> ExecuteAsync(string userId, UpdateProfileCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return new UpdateProfileResult.NotFound();

        if (cmd.DisplayName is not null)
        {
            if (!AuthValidation.IsValidDisplayName(cmd.DisplayName)) return new UpdateProfileResult.ValidationError("displayName");
            user.DisplayName = cmd.DisplayName.Trim();
        }

        if (cmd.Language is not null)
        {
            if (!AuthValidation.IsValidLanguage(cmd.Language)) return new UpdateProfileResult.ValidationError("language");
            user.Language = cmd.Language;
        }

        if (cmd.FavoriteClubId is not null)
        {
            if (!await _clubs.ExistsAsync(cmd.FavoriteClubId, ct)) return new UpdateProfileResult.InvalidClub();
            user.FavoriteClubId = cmd.FavoriteClubId;
        }

        user.ChangedAt = _now();
        await _users.UpdateAsync(user, ct);
        return new UpdateProfileResult.Success(user);
    }
}
