using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetShortlistUseCaseTests
{
    private readonly Mock<IShortlistRepository> _shortlist = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerStatsAggregator> _stats = new();

    private GetShortlistUseCase CreateSut(int maxSize = 20) =>
        new(_shortlist.Object, _players.Object, _stats.Object, new ShortlistSettings(maxSize));

    private static Player AnyPlayer(string id) => new(
        id, "Aron", "23", null, 35, "385-karlar", "385", "Stjarnan", "karlar", "VS", false, "LB");

    [Fact]
    public async Task ResolvedPlayer_IsEnriched_PriceAndPkNull_CountAndMaxReturned()
    {
        var created = DateTimeOffset.UnixEpoch;
        _shortlist.Setup(r => r.ListActiveAsync("u-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new[] { new ShortlistEntry("p-1", created, null) });
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1"));
        _stats.Setup(s => s.AggregateAsync(
                  "p-1", null, null, null, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AggregatedStats(
                  Games: 10, Goals: 20, YellowCards: 1, TwoMinuteSuspensions: 2, RedCards: 0,
                  Assists: 5, Steals: 3, Blocks: 1, Saves: 0, Turnovers: 4, LegalStops: 2, Shots: 30,
                  ExpectedGoals: 18.5, ShotsFaced: 0, SavePct: null, ExpectedSaves: 0,
                  GradeTotal: 7.2, GradeOffense: 7.5, GradeDefense: 6.9, GradeGoalkeeping: null));

        var view = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        var item = Assert.Single(view.Items);
        Assert.Equal("p-1", item.PlayerId);
        Assert.Equal("Aron", item.Name);
        Assert.Equal("Stjarnan", item.ClubName);
        Assert.Equal("VS", item.Position);
        Assert.Equal("LB", item.PositionSecondary);
        Assert.Equal("karlar", item.Gender);
        Assert.Null(item.Price);
        Assert.Null(item.PickPercentage);
        Assert.Equal(created, item.CreatedAt);
        Assert.Equal(1, view.Count);
        Assert.Equal(20, view.Max);
        Assert.Equal(10, item.Games);
        Assert.Equal(20, item.Goals);
        Assert.Equal(5, item.Assists);
        Assert.Equal(3, item.Steals);
        Assert.Equal(1, item.Blocks);
        Assert.Equal(4, item.Turnovers);
        Assert.Equal(2, item.LegalStops);
        Assert.Equal(30, item.Shots);
        Assert.Equal(18.5, item.ExpectedGoals);
        Assert.Equal(7.2, item.GradeTotal);
        Assert.Null(item.GradeGoalkeeping);
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
        Assert.Null(item.PositionSecondary);
        Assert.Null(item.Gender);
        Assert.Null(item.Games);
        Assert.Null(item.Assists);
        Assert.Equal(created, item.CreatedAt);
        _stats.Verify(s => s.AggregateAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()), Times.Never);
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
