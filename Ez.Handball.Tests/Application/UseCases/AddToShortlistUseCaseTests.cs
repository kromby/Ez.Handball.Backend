using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class AddToShortlistUseCaseTests
{
    private readonly Mock<IShortlistRepository> _shortlist = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(10);

    private AddToShortlistUseCase CreateSut(int maxSize = 20) =>
        new(_shortlist.Object, _players.Object, new ShortlistSettings(maxSize), () => Now);

    private static Player AnyPlayer(string id) => new(
        id, "Aron", "23", null, null, "385-karlar", "385", "Stjarnan", "karlar", "VS");

    // Helper: matches any UpsertAsync call.
    private static System.Linq.Expressions.Expression<Func<IShortlistRepository, Task>> AnyUpsert() =>
        r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>());

    [Fact]
    public async Task UnknownPlayer_ReturnsPlayerNotFound_AndDoesNotUpsert()
    {
        _players.Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("u-1", "nope", CancellationToken.None);

        Assert.IsType<AddToShortlistResult.PlayerNotFound>(result);
        _shortlist.Verify(AnyUpsert(), Times.Never);
    }

    [Fact]
    public async Task AlreadyActive_ReturnsAlreadyPresent_AndDoesNotUpsert()
    {
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1"));
        _shortlist.Setup(r => r.GetAsync("u-1", "p-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ShortlistEntry("p-1", DateTimeOffset.UnixEpoch, null));

        var result = await CreateSut().ExecuteAsync("u-1", "p-1", CancellationToken.None);

        Assert.IsType<AddToShortlistResult.AlreadyPresent>(result);
        _shortlist.Verify(AnyUpsert(), Times.Never);
    }

    [Fact]
    public async Task CapReached_ReturnsCapReached_WithMax()
    {
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1"));
        _shortlist.Setup(r => r.GetAsync("u-1", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShortlistEntry?)null);
        _shortlist.Setup(r => r.CountActiveAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await CreateSut(maxSize: 3).ExecuteAsync("u-1", "p-1", CancellationToken.None);

        var cap = Assert.IsType<AddToShortlistResult.CapReached>(result);
        Assert.Equal(3, cap.Max);
        _shortlist.Verify(AnyUpsert(), Times.Never);
    }

    [Fact]
    public async Task NewPlayer_UnderCap_Adds_WithNowAndNullDeletedAt()
    {
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1"));
        _shortlist.Setup(r => r.GetAsync("u-1", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShortlistEntry?)null);
        _shortlist.Setup(r => r.CountActiveAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(5);

        var result = await CreateSut().ExecuteAsync("u-1", "p-1", CancellationToken.None);

        Assert.IsType<AddToShortlistResult.Added>(result);
        _shortlist.Verify(r => r.UpsertAsync("u-1", "p-1", Now, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SoftDeletedPlayer_Reactivates_WithResetCreatedAt()
    {
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1"));
        _shortlist.Setup(r => r.GetAsync("u-1", "p-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ShortlistEntry("p-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)));
        _shortlist.Setup(r => r.CountActiveAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await CreateSut().ExecuteAsync("u-1", "p-1", CancellationToken.None);

        Assert.IsType<AddToShortlistResult.Added>(result);
        _shortlist.Verify(r => r.UpsertAsync("u-1", "p-1", Now, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
