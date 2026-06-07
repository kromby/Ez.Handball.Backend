using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetLeaderboardUseCaseTests
{
    private readonly Mock<ILeaderboardRepository> _repo = new();
    private readonly Mock<ITournamentScopeResolver> _scope = new();

    private GetLeaderboardUseCase CreateSut() => new(_repo.Object, _scope.Object);

    private static LeaderboardEntry Entry(int rank, string playerId) =>
        new(rank, playerId, $"P{playerId}", "385", "Stjarnan", "karlar",
            10, 100 - rank, 0, 0, 0, (100 - rank) / 10.0);

    private static LeaderboardRequest Req(
        LeaderboardMetric metric = LeaderboardMetric.Goals,
        string? season = null, string? tournamentId = null,
        string? competitionId = null, TournamentType? type = null, string? gender = null) =>
        new(metric, season, tournamentId, competitionId, type, gender);

    private void SetupRanked(params LeaderboardEntry[] entries) =>
        _repo.Setup(r => r.GetRankedAsync(It.IsAny<LeaderboardQuery>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(entries);

    private void SetupResolver(IReadOnlyList<string>? ids) =>
        _scope.Setup(s => s.ResolveTournamentIdsAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ids);

    [Fact]
    public async Task ExecuteAsync_SlicesRequestedPage_AndReportsFullTotal()
    {
        SetupResolver(null);
        SetupRanked(Entry(1, "a"), Entry(2, "b"), Entry(3, "c"), Entry(4, "d"), Entry(5, "e"));

        var result = await CreateSut().ExecuteAsync(Req(), offset: 1, limit: 2, CancellationToken.None);

        Assert.Equal(5, result.Total);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("b", result.Entries[0].PlayerId);
        Assert.Equal("c", result.Entries[1].PlayerId);
    }

    [Fact]
    public async Task ExecuteAsync_PassesResolvedTournamentIdsToRepo()
    {
        SetupResolver(new[] { "8444", "8427" });
        LeaderboardQuery? captured = null;
        _repo.Setup(r => r.GetRankedAsync(It.IsAny<LeaderboardQuery>(), It.IsAny<CancellationToken>()))
             .Callback<LeaderboardQuery, CancellationToken>((q, _) => captured = q)
             .ReturnsAsync(Array.Empty<LeaderboardEntry>());

        await CreateSut().ExecuteAsync(
            Req(season: "2025-26", competitionId: "olis-karla"), offset: 0, limit: 50, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(new[] { "8444", "8427" }, captured!.TournamentIds);
        Assert.Equal("2025-26", captured.Season);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsScopeArgumentsToResolver()
    {
        SetupResolver(null);
        SetupRanked();

        await CreateSut().ExecuteAsync(
            Req(season: "2025-26", tournamentId: "8427", competitionId: null, type: TournamentType.Playoffs),
            offset: 0, limit: 50, CancellationToken.None);

        _scope.Verify(s => s.ResolveTournamentIdsAsync(
            "2025-26", "8427", null, TournamentType.Playoffs, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EchoesMetricAsEnumName()
    {
        SetupResolver(null);
        SetupRanked();

        var result = await CreateSut().ExecuteAsync(
            Req(LeaderboardMetric.YellowCards), offset: 0, limit: 50, CancellationToken.None);

        Assert.Equal("YellowCards", result.Metric);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRanking_ReturnsEmptyPageAndZeroTotal()
    {
        SetupResolver(null);
        SetupRanked();

        var result = await CreateSut().ExecuteAsync(Req(), offset: 0, limit: 50, CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Entries);
    }
}
