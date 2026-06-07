using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class PlayerStatsAggregatorTests
{
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<ISeasonRepository> _seasons = new();

    private PlayerStatsAggregator CreateSut() => new(_stats.Object, _seasons.Object);

    private static PlayerStat Stat(string season, string tournamentId, int goals) =>
        new("match", tournamentId, "T", season, "team", "Club", goals, 0, 0, 0);

    public PlayerStatsAggregatorTests()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true) });
        _stats.Setup(r => r.GetByPlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>());
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

        var result = await CreateSut().AggregateAsync("p1", "2025-26", null, default);

        Assert.Equal(2, result.Games);
        Assert.Equal(8, result.Goals);
    }

    [Fact]
    public async Task NullSeason_ResolvesCurrentSeason()
    {
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat> { Stat("2025-26", "8444", 4) });

        var result = await CreateSut().AggregateAsync("p1", null, null, default);

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

        var result = await CreateSut().AggregateAsync("p1", "2025-26", "8444", default);

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

        var result = await CreateSut().AggregateAsync("p1", null, null, default);

        Assert.Equal(0, result.Games);
        Assert.Equal(0, result.Goals);
    }
}
