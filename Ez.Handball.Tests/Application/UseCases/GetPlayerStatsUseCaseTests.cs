using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerStatsUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<ITournamentScopeResolver> _scope = new();
    private readonly Mock<IMatchRepository> _matches = new();
    private readonly Mock<IScoringRuleSetRepository> _ruleSets = new();

    private static readonly ScoringRuleSet RuleSet = new(
        GameFlavor.Fantasy, 2, GoalPoints: 2, YellowCardPoints: -1, TwoMinutePoints: -2,
        RedCardPoints: -5, AppearancePoints: 1, AssistPoints: 1, StealPoints: 1, BlockPoints: 1, SavePoints: 1);

    private GetPlayerStatsUseCase CreateSut() => new(
        _players.Object, _stats.Object, _scope.Object, _matches.Object,
        new FantasyPointsCalculator(new FantasyPlayerRatingFunction(), _ruleSets.Object));

    private static MatchListItem Match(string matchId, DateTimeOffset date, string homeTeamId, string awayTeamId) =>
        new(matchId, "1", date, null, "S",
            new MatchListTeam(homeTeamId, homeTeamId.Split('-')[0], $"Club {homeTeamId}", null, 30),
            new MatchListTeam(awayTeamId, awayTeamId.Split('-')[0], $"Club {awayTeamId}", null, 28));

    private static Player Player(string id) =>
        new(id, "Name", null, null, null, "team", "club", "Club", "karlar", "Back", false);

    private static PlayerStat Stat(string season, string tournamentId, int goals) =>
        new("p1", "match", tournamentId, "T", season, "team", "Club", goals, 0, 0, 0);

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
        _scope.Setup(s => s.ResolveSeasonLabelAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((string? s, CancellationToken _) => s);
        _ruleSets.Setup(r => r.GetAsync(GameFlavor.Fantasy, 2, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(RuleSet);
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
    public async Task NullSeason_DefaultsToCurrentSeason()
    {
        _scope.Setup(s => s.ResolveSeasonLabelAsync(null, It.IsAny<CancellationToken>()))
              .ReturnsAsync("2025-26");
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 5), Stat("2024-25", "8444", 3) });

        var result = await CreateSut().ExecuteAsync("p1", Query(), default);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Single(found.Stats);
        Assert.Equal("2025-26", found.Stats[0].Stat.Season);
    }

    [Fact]
    public async Task SeasonFilter_NarrowsBySeason()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 5), Stat("2024-25", "8444", 3) });

        var result = await CreateSut().ExecuteAsync("p1", Query(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Equal("2025-26", Assert.Single(found.Stats).Stat.Season);
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
        Assert.DoesNotContain(found.Stats, s => s.Stat.TournamentId == "9999");
    }

    [Fact]
    public async Task EmptyResolvedScope_ReturnsNoRows()
    {
        _scope.Setup(s => s.ResolveTournamentIdsAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<string>());
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 5) });

        var result = await CreateSut().ExecuteAsync(
            "p1", Query(season: "2025-26", competitionId: "no-such-comp"), default);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Empty(found.Stats);
    }

    [Fact]
    public async Task PlayerNotFound_DoesNotQueryStats()
    {
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player?)null);

        await CreateSut().ExecuteAsync("ghost", Query(), default);

        _stats.Verify(r => r.GetByPlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EachLine_CarriesOpponentDateAndPoints_NewestFirst()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  new("p1", "m1", "8444", "T", "2025-26", "453-karlar", "Valur", 5, 1, 0, 0, HbStatzAssists: 3, HbStatzSaves: 0),
                  new("p1", "m2", "8444", "T", "2025-26", "453-karlar", "Valur", 2, 0, 0, 0),
              });
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TournamentMatches("8444", "T", "2025-26", new[]
                {
                    Match("m1", new DateTimeOffset(2025, 9, 1, 0, 0, 0, TimeSpan.Zero), "453-karlar", "143-karlar"),
                    Match("m2", new DateTimeOffset(2025, 9, 8, 0, 0, 0, TimeSpan.Zero), "385-karlar", "453-karlar"),
                }));

        var result = await CreateSut().ExecuteAsync("p1", Query(season: "2025-26"), default);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Equal(new[] { "m2", "m1" }, found.Stats.Select(l => l.Stat.MatchId));
        Assert.Equal("385", found.Stats[0].Opponent!.ClubId);   // player was the away side
        Assert.Equal("143", found.Stats[1].Opponent!.ClubId);   // player was the home side
        // m1: 5 goals * 2 + 1 appearance - 1 yellow + 3 assists = 13
        Assert.Equal(13, found.Stats[1].Points);
        Assert.Equal(5, found.Stats[0].Points);
    }

    [Fact]
    public async Task MatchMissingFromListing_KeepsLineWithoutOpponent()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 1) });
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TournamentMatches?)null);

        var result = await CreateSut().ExecuteAsync("p1", Query(season: "2025-26"), default);

        var line = Assert.Single(Assert.IsType<GetPlayerStatsResult.Found>(result).Stats);
        Assert.Null(line.Opponent);
        Assert.Null(line.Date);
    }

    [Fact]
    public async Task MissingRuleSet_LeavesPointsNull()
    {
        _ruleSets.Setup(r => r.GetAsync(GameFlavor.Fantasy, 2, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((ScoringRuleSet?)null);
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 1) });

        var result = await CreateSut().ExecuteAsync("p1", Query(season: "2025-26"), default);

        Assert.Null(Assert.Single(Assert.IsType<GetPlayerStatsResult.Found>(result).Stats).Points);
    }
}
