using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class RemoveFromShortlistUseCaseTests
{
    private readonly Mock<IShortlistRepository> _shortlist = new();
    private static readonly DateTimeOffset Created = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(10);

    private RemoveFromShortlistUseCase CreateSut() => new(_shortlist.Object, () => Now);

    private static System.Linq.Expressions.Expression<Func<IShortlistRepository, Task>> AnyUpsert() =>
        r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>());

    [Fact]
    public async Task ActiveEntry_SoftDeleted_PreservingCreatedAt()
    {
        _shortlist.Setup(r => r.GetAsync("u-1", "p-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ShortlistEntry("p-1", Created, null));

        await CreateSut().ExecuteAsync("u-1", "p-1", CancellationToken.None);

        _shortlist.Verify(r => r.UpsertAsync("u-1", "p-1", Created, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AbsentEntry_IsNoOp()
    {
        _shortlist.Setup(r => r.GetAsync("u-1", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShortlistEntry?)null);

        await CreateSut().ExecuteAsync("u-1", "p-1", CancellationToken.None);

        _shortlist.Verify(AnyUpsert(), Times.Never);
    }

    [Fact]
    public async Task AlreadyDeletedEntry_IsNoOp()
    {
        _shortlist.Setup(r => r.GetAsync("u-1", "p-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ShortlistEntry("p-1", Created, Now));

        await CreateSut().ExecuteAsync("u-1", "p-1", CancellationToken.None);

        _shortlist.Verify(AnyUpsert(), Times.Never);
    }
}
