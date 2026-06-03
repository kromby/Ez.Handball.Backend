using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetShortlistUseCaseTests
{
    private readonly Mock<IShortlistRepository> _shortlist = new();
    private readonly Mock<IPlayerRepository> _players = new();

    private GetShortlistUseCase CreateSut(int maxSize = 20) =>
        new(_shortlist.Object, _players.Object, new ShortlistSettings(maxSize));

    private static Player AnyPlayer(string id) => new(
        id, "Aron", "23", null, 35, "385-karlar", "385", "Stjarnan", "karlar", "VS");

    [Fact]
    public async Task ResolvedPlayer_IsEnriched_PriceAndPkNull_CountAndMaxReturned()
    {
        var created = DateTimeOffset.UnixEpoch;
        _shortlist.Setup(r => r.ListActiveAsync("u-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new[] { new ShortlistEntry("p-1", created, null) });
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1"));

        var view = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        var item = Assert.Single(view.Items);
        Assert.Equal("p-1", item.PlayerId);
        Assert.Equal("Aron", item.Name);
        Assert.Equal("Stjarnan", item.ClubName);
        Assert.Equal("VS", item.Position);
        Assert.Equal("karlar", item.Gender);
        Assert.Null(item.Price);
        Assert.Null(item.PickPercentage);
        Assert.Equal(created, item.CreatedAt);
        Assert.Equal(1, view.Count);
        Assert.Equal(20, view.Max);
    }

    [Fact]
    public async Task UnresolvedPlayer_IsKept_WithNullEnrichment()
    {
        var created = DateTimeOffset.UnixEpoch;
        _shortlist.Setup(r => r.ListActiveAsync("u-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new[] { new ShortlistEntry("ghost", created, null) });
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var view = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        var item = Assert.Single(view.Items);
        Assert.Equal("ghost", item.PlayerId);
        Assert.Null(item.Name);
        Assert.Null(item.ClubName);
        Assert.Null(item.Position);
        Assert.Null(item.Gender);
        Assert.Equal(created, item.CreatedAt);
    }

    [Fact]
    public async Task EmptyShortlist_ReturnsEmptyItems_ZeroCount()
    {
        _shortlist.Setup(r => r.ListActiveAsync("u-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Array.Empty<ShortlistEntry>());

        var view = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        Assert.Empty(view.Items);
        Assert.Equal(0, view.Count);
    }
}
