using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerRatingUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerStatsAggregator> _aggregator = new();
    private readonly Mock<IScoringRuleSetRepository> _ruleSets = new();

    private static readonly ScoringRuleSet FantasyV1 =
        new(GameFlavor.Fantasy, 1, 2, -1, -2, -5, 1);

    private GetPlayerRatingUseCase CreateSut() => new(
        new IPlayerRatingFunction[] { new FantasyPlayerRatingFunction(), new ManagerPlayerRatingFunction() },
        _players.Object, _aggregator.Object, _ruleSets.Object);

    private static Player Player(string id) =>
        new(id, "Name", null, null, null, "team", "club", "Club", "karlar", "Back", false);

    private static PlayerRatingContext Ctx(
        string? season = null, string? tournamentId = null, int? ruleSetVersion = null) =>
        new(season, tournamentId, null, ruleSetVersion, null, null);

    private void Aggregate(int games, int goals) =>
        _aggregator.Setup(a => a.AggregateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AggregatedStats(games, goals, 0, 0, 0));

    public GetPlayerRatingUseCaseTests()
    {
        _players.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Player(id));
        Aggregate(0, 0);
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
    public async Task Fantasy_ComputesValueFromAggregatedStats()
    {
        Aggregate(games: 2, goals: 8);

        var result = await CreateSut().ExecuteAsync("p1", GameFlavor.Fantasy, Ctx(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        Assert.Equal(18, found.Rating.Rating);
        Assert.Equal("fantasy-v1", found.Rating.Version);
    }

    [Fact]
    public async Task Fantasy_PassesScopeToAggregator()
    {
        Aggregate(games: 1, goals: 5);

        await CreateSut().ExecuteAsync(
            "p1", GameFlavor.Fantasy, Ctx(season: "2025-26", tournamentId: "8444"), default);

        _aggregator.Verify(a => a.AggregateAsync("p1", "2025-26", "8444", It.IsAny<string?>(), It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()), Times.Once);
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
        Aggregate(games: 1, goals: 4);

        var result = await CreateSut().ExecuteAsync(
            "p1", GameFlavor.Manager, Ctx(season: "2025-26", ruleSetVersion: 2), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        Assert.Equal("manager-v0", found.Rating.Version);
        Assert.Equal(5, found.Rating.Rating);
        _ruleSets.Verify(
            r => r.GetAsync(It.IsAny<GameFlavor>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_NeedsNoRuleSet_AndDoesNotQueryRuleSets()
    {
        Aggregate(games: 1, goals: 4);

        var result = await CreateSut().ExecuteAsync("p1", GameFlavor.Manager, Ctx(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerRatingResult.Found>(result);
        Assert.Equal("manager-v0", found.Rating.Version);
        Assert.Equal(5, found.Rating.Rating);
        _ruleSets.Verify(
            r => r.GetAsync(It.IsAny<GameFlavor>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
