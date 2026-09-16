using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class PlayerStatsAggregatorTests
{
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<ISeasonRepository> _seasons = new();
    private readonly Mock<ITournamentRepository> _tournaments = new();

    private PlayerStatsAggregator CreateSut()
    {
        var scope = new TournamentScopeResolver(_tournaments.Object, _seasons.Object);
        return new PlayerStatsAggregator(_stats.Object, scope);
    }

    private static PlayerStat Stat(string season, string tournamentId, int goals) =>
        new("p1", "match", tournamentId, "T", season, "team", "Club", goals, 0, 0, 0);

    private static PlayerStat StatWithHbStatz(
        string season, string tournamentId, int? assists, int? steals, int? blocks, int? saves) =>
        new("p1", "match", tournamentId, "T", season, "team", "Club", 0, 0, 0, 0,
            HbStatzAssists: assists, HbStatzSteals: steals, HbStatzBlocks: blocks, HbStatzSaves: saves);

    private void SetupTournamentsBySeason(string season, params Tournament[] rows) =>
        _tournaments.Setup(r => r.ListBySeasonAsync(season, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows);

    private static Tournament Trn(string id, TournamentType type, string competitionId) =>
        new(id, $"name-{id}", "karlar", type, competitionId, $"comp-{competitionId}");

    public PlayerStatsAggregatorTests()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true) });
        _stats.Setup(r => r.GetByPlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>());
        _tournaments.Setup(r => r.ListBySeasonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament>());
    }

    [Fact]
    public async Task ExplicitSeason_SumsScopedRows()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2025-26", "8444", 5),
                  Stat("2025-26", "8444", 3),
                  Stat("2024-25", "8444", 9),
              });

        var result = await CreateSut().AggregateAsync("p1", "2025-26", null, null, null, default);

        Assert.Equal(2, result.Games);
        Assert.Equal(8, result.Goals);
    }

    [Fact]
    public async Task NullSeason_ResolvesCurrentSeason()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().AggregateAsync("p1", null, null, null, null, default);

        Assert.Equal(1, result.Games);
        Assert.Equal(4, result.Goals);
    }

    [Fact]
    public async Task TournamentScope_FiltersByTournament()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2025-26", "8444", 5),
                  Stat("2025-26", "9999", 3),
              });

        var result = await CreateSut().AggregateAsync("p1", "2025-26", "8444", null, null, default);

        Assert.Equal(1, result.Games);
        Assert.Equal(5, result.Goals);
    }

    [Fact]
    public async Task NoCurrentSeason_ReturnsZeroStats()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false) });
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2024-25", "8444", 9) });

        var result = await CreateSut().AggregateAsync("p1", null, null, null, null, default);

        Assert.Equal(0, result.Games);
        Assert.Equal(0, result.Goals);
    }

    [Fact]
    public async Task CompetitionScope_AggregatesAcrossPhases()
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
                  Stat("2025-26", "9999", 7),
              });

        var result = await CreateSut().AggregateAsync("p1", "2025-26", null, "olis-karla", null, default);

        Assert.Equal(2, result.Games);
        Assert.Equal(8, result.Goals);
    }

    [Fact]
    public async Task TypeScope_NarrowsToPhase()
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

        var result = await CreateSut().AggregateAsync("p1", "2025-26", null, null, TournamentType.Playoffs, default);

        Assert.Equal(1, result.Games);
        Assert.Equal(3, result.Goals);
    }

    [Fact]
    public async Task SumsHbStatzFields_DefaultingNullToZero()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  StatWithHbStatz("2025-26", "8444", assists: 2, steals: 1, blocks: null, saves: null),
                  StatWithHbStatz("2025-26", "8444", assists: null, steals: 3, blocks: 1, saves: 8),
              });

        var result = await CreateSut().AggregateAsync("p1", "2025-26", null, null, null, default);

        Assert.Equal(2, result.Assists);
        Assert.Equal(4, result.Steals);
        Assert.Equal(1, result.Blocks);
        Assert.Equal(8, result.Saves);
    }

    [Fact]
    public async Task SumsExtendedHbStatzFields_AndDerivesSavePctAndGradeAverages()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  new("p1", "m1", "8444", "T", "2025-26", "team", "Club", 0, 0, 0, 0,
                      HbStatzTurnovers: 1, HbStatzLegalStops: 2, HbStatzShots: 6,
                      HbStatzExpectedGoals: 4.5, HbStatzSaves: 3, HbStatzShotsFaced: 10,
                      HbStatzExpectedSaves: 2.5, HbStatzGradeTotal: 7.0, HbStatzGradeOffense: 6.5,
                      HbStatzGradeDefense: 7.5, HbStatzGradeGoalkeeping: null),
                  new("p1", "m2", "8444", "T", "2025-26", "team", "Club", 0, 0, 0, 0,
                      HbStatzSaves: 1, HbStatzShotsFaced: 5, HbStatzGradeTotal: 8.0),
              });

        var result = await CreateSut().AggregateAsync("p1", "2025-26", null, null, null, default);

        Assert.Equal(1, result.Turnovers);
        Assert.Equal(2, result.LegalStops);
        Assert.Equal(6, result.Shots);
        Assert.Equal(4.5, result.ExpectedGoals);
        Assert.Equal(15, result.ShotsFaced);
        Assert.Equal(26.7, result.SavePct); // (3+1)/15 * 100, rounded to 1dp
        Assert.Equal(2.5, result.ExpectedSaves);
        Assert.Equal(7.5, result.GradeTotal); // (7.0 + 8.0) / 2
        Assert.Equal(6.5, result.GradeOffense);
        Assert.Null(result.GradeGoalkeeping);
    }

    [Fact]
    public async Task AggregatePreviousSeason_NoPreviousSeasonExists_ReturnsNull()
    {
        // constructor default: only "2025-26" is tracked -> no previous season
        var result = await CreateSut().AggregatePreviousSeasonAsync("p1", "2025-26", "8444", null, null, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task AggregatePreviousSeason_SameCompetitionPriorSeason_SumsScopedRows()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupTournamentsBySeason("2025-26", Trn("8444", TournamentType.League, "olis-karla"));
        SetupTournamentsBySeason("2024-25",
            Trn("7777", TournamentType.League, "olis-karla"),
            Trn("6666", TournamentType.Cup, "bikar-karla"));
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2024-25", "7777", 5),
                  Stat("2024-25", "7777", 3),
                  Stat("2024-25", "6666", 99),   // decoy: different competition, must be excluded
              });

        var result = await CreateSut().AggregatePreviousSeasonAsync("p1", "2025-26", "8444", null, null, default);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Games);
        Assert.Equal(8, result.Goals);
    }

    [Fact]
    public async Task AggregatePreviousSeason_NoQualifyingRows_ReturnsNull()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupTournamentsBySeason("2025-26", Trn("8444", TournamentType.League, "olis-karla"));
        SetupTournamentsBySeason("2024-25", Trn("7777", TournamentType.League, "olis-karla"));
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>());

        var result = await CreateSut().AggregatePreviousSeasonAsync("p1", "2025-26", "8444", null, null, default);

        Assert.Null(result);
    }
}
