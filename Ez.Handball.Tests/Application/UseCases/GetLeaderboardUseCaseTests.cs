using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetLeaderboardUseCaseTests
{
    private readonly Mock<ILeaderboardRepository> _repo = new();

    private GetLeaderboardUseCase CreateSut() => new(_repo.Object);

    private static LeaderboardEntry Entry(int rank, string playerId) =>
        new(rank, playerId, $"P{playerId}", "385", "Stjarnan", "karlar",
            10, 100 - rank, 0, 0, 0, (100 - rank) / 10.0);

    private static LeaderboardQuery Query(LeaderboardMetric metric = LeaderboardMetric.Goals) =>
        new(metric, null, null, null);

    private void SetupRanked(params LeaderboardEntry[] entries) =>
        _repo.Setup(r => r.GetRankedAsync(It.IsAny<LeaderboardQuery>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(entries);

    [Fact]
    public async Task ExecuteAsync_SlicesRequestedPage_AndReportsFullTotal()
    {
        SetupRanked(Entry(1, "a"), Entry(2, "b"), Entry(3, "c"), Entry(4, "d"), Entry(5, "e"));

        var result = await CreateSut().ExecuteAsync(Query(), offset: 1, limit: 2, CancellationToken.None);

        Assert.Equal(5, result.Total);
        Assert.Equal(1, result.Offset);
        Assert.Equal(2, result.Limit);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("b", result.Entries[0].PlayerId);
        Assert.Equal("c", result.Entries[1].PlayerId);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesAbsoluteRanksAcrossPages()
    {
        SetupRanked(Entry(1, "a"), Entry(2, "b"), Entry(3, "c"), Entry(4, "d"));

        var result = await CreateSut().ExecuteAsync(Query(), offset: 2, limit: 2, CancellationToken.None);

        Assert.Equal(3, result.Entries[0].Rank);
        Assert.Equal(4, result.Entries[1].Rank);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRanking_ReturnsEmptyPageAndZeroTotal()
    {
        SetupRanked();

        var result = await CreateSut().ExecuteAsync(Query(), offset: 0, limit: 50, CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_OffsetPastEnd_ReturnsEmptyEntriesButTrueTotal()
    {
        SetupRanked(Entry(1, "a"), Entry(2, "b"));

        var result = await CreateSut().ExecuteAsync(Query(), offset: 50, limit: 50, CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_EchoesMetricAsEnumName()
    {
        SetupRanked();

        var result = await CreateSut().ExecuteAsync(
            Query(LeaderboardMetric.YellowCards), offset: 0, limit: 50, CancellationToken.None);

        Assert.Equal("YellowCards", result.Metric);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        SetupRanked();

        await CreateSut().ExecuteAsync(Query(), offset: 0, limit: 50, cts.Token);

        _repo.Verify(r => r.GetRankedAsync(It.IsAny<LeaderboardQuery>(), cts.Token), Times.Once);
    }
}
