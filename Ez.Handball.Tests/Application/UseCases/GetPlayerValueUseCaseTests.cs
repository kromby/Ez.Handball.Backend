using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Application.ValueFunctions;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerValueUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<ISeasonRepository> _seasons = new();
    private readonly Mock<IScoringRuleSetRepository> _ruleSets = new();
    private readonly Mock<ITournamentRepository> _tournaments = new();

    private static readonly ScoringRuleSet FantasyV1 =
        new(ValueFlavor.Fantasy, 1, 2, -1, -2, -5, 1);

    private GetPlayerValueUseCase CreateSut()
    {
        var scope = new Ez.Handball.Application.Services.TournamentScopeResolver(
            _tournaments.Object, _seasons.Object);
        return new(
            new IPlayerValueFunction[] { new FantasyPlayerValueFunction(), new ManagerPlayerValueFunction() },
            _players.Object, _stats.Object, _seasons.Object, _ruleSets.Object, scope);
    }

    private static Player Player(string id) =>
        new(id, "Name", null, null, null, "team", "club", "Club", "karlar", "Back");

    private static PlayerStat Stat(string season, string tournamentId, int goals) =>
        new("match", tournamentId, "T", season, "team", "Club", goals, 0, 0, 0);

    private void SetupTournamentsBySeason(string season, params Tournament[] rows) =>
        _tournaments.Setup(r => r.ListBySeasonAsync(season, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows);

    private static Tournament Trn(string id, TournamentType type, string competitionId) =>
        new(id, $"name-{id}", "karlar", type, competitionId, $"comp-{competitionId}");

    private static PlayerValueContext Ctx(
        string? season = null, string? tournamentId = null, string? competitionId = null,
        int? ruleSetVersion = null, TournamentType? type = null) =>
        new(season, tournamentId, competitionId, ruleSetVersion, type, null);

    public GetPlayerValueUseCaseTests()
    {
        _players.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Player(id));
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true) });
        _stats.Setup(r => r.GetByPlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>());
        _ruleSets.Setup(r => r.GetAsync(ValueFlavor.Fantasy, 1, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(FantasyV1);
    }

    [Fact]
    public async Task PlayerNotFound_ReturnsNotFound()
    {
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("ghost", ValueFlavor.Fantasy, Ctx(), default);

        Assert.IsType<GetPlayerValueResult.NotFound>(result);
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

        var result = await CreateSut().ExecuteAsync("p1", ValueFlavor.Fantasy, Ctx(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        // 2 games, 8 goals: 8*2 + 2*1 = 18
        Assert.Equal(18, found.Value.Value);
        Assert.Equal("fantasy-v1", found.Value.Version);
    }

    [Fact]
    public async Task Fantasy_NullSeason_ResolvesCurrentSeason()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().ExecuteAsync("p1", ValueFlavor.Fantasy, Ctx(season: null), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        // 1 game, 4 goals: 4*2 + 1*1 = 9
        Assert.Equal(9, found.Value.Value);
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
            "p1", ValueFlavor.Fantasy, Ctx(season: "2025-26", tournamentId: "8444"), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        // 1 game, 5 goals: 5*2 + 1*1 = 11
        Assert.Equal(11, found.Value.Value);
    }

    [Fact]
    public async Task Fantasy_NoCurrentSeason_ComputesOverZeroStats()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false) });
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2024-25", "8444", 9) });

        var result = await CreateSut().ExecuteAsync("p1", ValueFlavor.Fantasy, Ctx(season: null), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        Assert.Equal(0, found.Value.Value);
    }

    [Fact]
    public async Task Fantasy_ExplicitRuleSetVersion_IsUsed()
    {
        await CreateSut().ExecuteAsync("p1", ValueFlavor.Fantasy, Ctx(ruleSetVersion: 1), default);

        _ruleSets.Verify(r => r.GetAsync(ValueFlavor.Fantasy, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fantasy_MissingRuleSet_ReturnsRuleSetNotFound()
    {
        _ruleSets.Setup(r => r.GetAsync(ValueFlavor.Fantasy, 1, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((ScoringRuleSet?)null);

        var result = await CreateSut().ExecuteAsync("p1", ValueFlavor.Fantasy, Ctx(), default);

        Assert.IsType<GetPlayerValueResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task Manager_WithExplicitRuleSetVersion_IgnoresIt_AndNeverQueriesRuleSets()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().ExecuteAsync(
            "p1", ValueFlavor.Manager, Ctx(season: "2025-26", ruleSetVersion: 2), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        Assert.Equal("manager-v0", found.Value.Version);
        // rating = goals + games = 4 + 1 = 5
        Assert.Equal(5, found.Value.Value);
        _ruleSets.Verify(
            r => r.GetAsync(It.IsAny<ValueFlavor>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_NeedsNoRuleSet_AndDoesNotQueryRuleSets()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().ExecuteAsync("p1", ValueFlavor.Manager, Ctx(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        Assert.Equal("manager-v0", found.Value.Version);
        // rating = goals + games = 4 + 1 = 5
        Assert.Equal(5, found.Value.Value);
        _ruleSets.Verify(
            r => r.GetAsync(It.IsAny<ValueFlavor>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Fantasy_CompetitionScope_AggregatesAcrossPhases()
    {
        SetupTournamentsBySeason("2025-26",
            Trn("8444", TournamentType.League, "olis-karla"),
            Trn("8427", TournamentType.Playoffs, "olis-karla"),
            Trn("9999", TournamentType.Cup, "bikar-karla"));
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2025-26", "8444", 5),
                  Stat("2025-26", "8427", 3),
                  Stat("2025-26", "9999", 7), // different competition — excluded
              });

        var result = await CreateSut().ExecuteAsync(
            "p1", ValueFlavor.Fantasy, Ctx(season: "2025-26", competitionId: "olis-karla"), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        // 2 games, 8 goals: 8*2 + 2*1 = 18
        Assert.Equal(18, found.Value.Value);
    }

    [Fact]
    public async Task Fantasy_TypeScope_NarrowsToPhase()
    {
        SetupTournamentsBySeason("2025-26",
            Trn("8444", TournamentType.League, "olis-karla"),
            Trn("8427", TournamentType.Playoffs, "olis-karla"));
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2025-26", "8444", 5),
                  Stat("2025-26", "8427", 3),
              });

        var result = await CreateSut().ExecuteAsync(
            "p1", ValueFlavor.Fantasy, Ctx(season: "2025-26", type: TournamentType.Playoffs), default);

        var found = Assert.IsType<GetPlayerValueResult.Found>(result);
        // playoffs only: 1 game, 3 goals: 3*2 + 1 = 7
        Assert.Equal(7, found.Value.Value);
    }
}
