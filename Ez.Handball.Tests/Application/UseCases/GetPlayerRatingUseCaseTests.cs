using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerRatingUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<ISeasonRepository> _seasons = new();
    private readonly Mock<IScoringRuleSetRepository> _ruleSets = new();

    private static readonly ScoringRuleSet FantasyV1 =
        new(GameFlavor.Fantasy, 1, 2, -1, -2, -5, 1);

    private GetPlayerRatingUseCase CreateSut() => new(
        new IPlayerRatingFunction[] { new FantasyPlayerRatingFunction(), new ManagerPlayerRatingFunction() },
        _players.Object, _stats.Object, _seasons.Object, _ruleSets.Object);

    private static Player Player(string id) =>
        new(id, "Name", null, null, null, "team", "club", "Club", "karlar", "Back");

    private static PlayerStat Stat(string season, string tournamentId, int goals) =>
        new("match", tournamentId, "T", season, "team", "Club", goals, 0, 0, 0);

    private static PlayerRatingContext Ctx(
        string? season = null, string? tournamentId = null, int? ruleSetVersion = null) =>
        new(season, tournamentId, ruleSetVersion, null, null);

    public GetPlayerRatingUseCaseTests()
    {
        _players.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Player(id));
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true) });
        _stats.Setup(r => r.GetByPlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>());
        _ruleSets.Setup(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(FantasyV1);
    }

    [Fact]
    public async Task PlayerNotFound_ReturnsNotFound()
    {
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("ghost", GameFlavor.Fantasy, Ctx(), default);

        Assert.IsType<GetPlayerRatingResult.NotFound>(result);
    }

    [Fact]
    public async Task Fantasy_AggregatesScopedStats_AndComputesValue()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2025-26", "8444", 5),
                  Stat("2025-26", "8444", 3),
                  Stat("2024-25", "8444", 9), // different season — excluded
              });

        var result = await CreateSut().ExecuteAsync("p1", GameFlavor.Fantasy, Ctx(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        // 2 games, 8 goals: 8*2 + 2*1 = 18
        Assert.Equal(18, found.Rating.Rating);
        Assert.Equal("fantasy-v1", found.Rating.Version);
    }

    [Fact]
    public async Task Fantasy_NullSeason_ResolvesCurrentSeason()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().ExecuteAsync("p1", GameFlavor.Fantasy, Ctx(season: null), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        // 1 game, 4 goals: 4*2 + 1*1 = 9
        Assert.Equal(9, found.Rating.Rating);
    }

    [Fact]
    public async Task Fantasy_TournamentScope_FiltersByTournament()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2025-26", "8444", 5),
                  Stat("2025-26", "9999", 3), // other tournament — excluded
              });

        var result = await CreateSut().ExecuteAsync(
            "p1", GameFlavor.Fantasy, Ctx(season: "2025-26", tournamentId: "8444"), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        // 1 game, 5 goals: 5*2 + 1*1 = 11
        Assert.Equal(11, found.Rating.Rating);
    }

    [Fact]
    public async Task Fantasy_NoCurrentSeason_ComputesOverZeroStats()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false) });
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2024-25", "8444", 9) });

        var result = await CreateSut().ExecuteAsync("p1", GameFlavor.Fantasy, Ctx(season: null), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        Assert.Equal(0, found.Rating.Rating);
    }

    [Fact]
    public async Task Fantasy_ExplicitRuleSetVersion_IsUsed()
    {
        await CreateSut().ExecuteAsync("p1", GameFlavor.Fantasy, Ctx(ruleSetVersion: 1), default);

        _ruleSets.Verify(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fantasy_MissingRuleSet_ReturnsRuleSetNotFound()
    {
        _ruleSets.Setup(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((ScoringRuleSet?)null);

        var result = await CreateSut().ExecuteAsync("p1", GameFlavor.Fantasy, Ctx(), default);

        Assert.IsType<GetPlayerRatingResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task Manager_WithExplicitRuleSetVersion_IgnoresIt_AndNeverQueriesRuleSets()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().ExecuteAsync(
            "p1", GameFlavor.Manager, Ctx(season: "2025-26", ruleSetVersion: 2), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        Assert.Equal("manager-v0", found.Rating.Version);
        // rating = goals + games = 4 + 1 = 5
        Assert.Equal(5, found.Rating.Rating);
        _ruleSets.Verify(
            r => r.GetAsync(It.IsAny<GameFlavor>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_NeedsNoRuleSet_AndDoesNotQueryRuleSets()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().ExecuteAsync("p1", GameFlavor.Manager, Ctx(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        Assert.Equal("manager-v0", found.Rating.Version);
        // rating = goals + games = 4 + 1 = 5
        Assert.Equal(5, found.Rating.Rating);
        _ruleSets.Verify(
            r => r.GetAsync(It.IsAny<GameFlavor>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
