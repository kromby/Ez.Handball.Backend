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
        new("match", tournamentId, "T", season, "team", "Club", goals, 0, 0, 0);

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
}
