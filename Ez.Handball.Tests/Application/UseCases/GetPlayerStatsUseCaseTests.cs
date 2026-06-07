using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerStatsUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<ITournamentScopeResolver> _scope = new();

    private GetPlayerStatsUseCase CreateSut() => new(_players.Object, _stats.Object, _scope.Object);

    private static Player Player(string id) =>
        new(id, "Name", null, null, null, "team", "club", "Club", "karlar", "Back");

    private static PlayerStat Stat(string season, string tournamentId, int goals) =>
        new("match", tournamentId, "T", season, "team", "Club", goals, 0, 0, 0);

    private static PlayerStatsQuery Query(
        string? season = null, string? tournamentId = null,
        string? competitionId = null, TournamentType? type = null) =>
        new(season, tournamentId, competitionId, type);

    public GetPlayerStatsUseCaseTests()
    {
        _players.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Player(id));
        _scope.Setup(s => s.ResolveTournamentIdsAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((IReadOnlyList<string>?)null);
    }

    [Fact]
    public async Task PlayerNotFound_ReturnsNotFound()
    {
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("ghost", Query(), default);

        Assert.IsType<GetPlayerStatsResult.NotFound>(result);
    }

    [Fact]
    public async Task NoFilters_ReturnsAllRows()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 5), Stat("2024-25", "8444", 3) });

        var result = await CreateSut().ExecuteAsync("p1", Query(), default);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Equal(2, found.Stats.Count);
    }

    [Fact]
    public async Task SeasonFilter_NarrowsBySeason()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 5), Stat("2024-25", "8444", 3) });

        var result = await CreateSut().ExecuteAsync("p1", Query(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Equal("2025-26", Assert.Single(found.Stats).Season);
    }

    [Fact]
    public async Task CompetitionFilter_NarrowsByResolvedTournamentIds()
    {
        _scope.Setup(s => s.ResolveTournamentIdsAsync(
                  "2025-26", null, "olis-karla", null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "8444", "8427" });
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2025-26", "8444", 5),
                  Stat("2025-26", "8427", 3),
                  Stat("2025-26", "9999", 7), // other competition — excluded
              });

        var result = await CreateSut().ExecuteAsync(
            "p1", Query(season: "2025-26", competitionId: "olis-karla"), default);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Equal(2, found.Stats.Count);
        Assert.DoesNotContain(found.Stats, s => s.TournamentId == "9999");
    }
}
