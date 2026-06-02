using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class UpdateProfileUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClubRepository> _clubs = new();

    private UpdateProfileUseCase CreateSut() => new(_users.Object, _clubs.Object, () => Now);

    private UserEntity Existing() => new()
    {
        RowKey = "u-1", Email = "a@b.is", DisplayName = "Jón", Language = "is", FavoriteClubId = "385",
        ChangedAt = DateTimeOffset.UnixEpoch
    };

    [Fact]
    public async Task UpdatesEachProvidedFieldIndependently_AndBumpsChangedAt()
    {
        var user = Existing();
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateSut().ExecuteAsync(
            "u-1", new UpdateProfileCommand(DisplayName: "Páll", Language: null, FavoriteClubId: null), CancellationToken.None);

        var success = Assert.IsType<UpdateProfileResult.Success>(result);
        Assert.Equal("Páll", success.User.DisplayName);
        Assert.Equal("is", success.User.Language);
        Assert.Equal("385", success.User.FavoriteClubId);
        Assert.Equal(Now, user.ChangedAt);
    }

    [Fact]
    public async Task ChangingClub_ValidatesItExists()
    {
        var user = Existing();
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _clubs.Setup(c => c.ExistsAsync("999", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync(
            "u-1", new UpdateProfileCommand(null, null, "999"), CancellationToken.None);

        Assert.IsType<UpdateProfileResult.Success>(result);
        Assert.Equal("999", user.FavoriteClubId);
    }

    [Fact]
    public async Task InvalidClub_ReturnsInvalidClub_AndDoesNotUpdate()
    {
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(Existing());
        _clubs.Setup(c => c.ExistsAsync("999", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync(
            "u-1", new UpdateProfileCommand(null, null, "999"), CancellationToken.None);

        Assert.IsType<UpdateProfileResult.InvalidClub>(result);
        _users.Verify(u => u.UpdateAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidLanguage_ReturnsValidationError()
    {
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(Existing());

        var result = await CreateSut().ExecuteAsync(
            "u-1", new UpdateProfileCommand(null, "de", null), CancellationToken.None);

        var err = Assert.IsType<UpdateProfileResult.ValidationError>(result);
        Assert.Equal("language", err.Field);
    }

    [Fact]
    public async Task MissingUser_ReturnsNotFound()
    {
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((UserEntity?)null);

        var result = await CreateSut().ExecuteAsync("u-1", new UpdateProfileCommand("X", null, null), CancellationToken.None);

        Assert.IsType<UpdateProfileResult.NotFound>(result);
    }
}
