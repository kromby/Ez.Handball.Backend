using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TablePlayerStatsRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private IPlayerStatsRepository CreateSut() =>
        new TablePlayerStatsRepository(_query.Object, NullLogger<TablePlayerStatsRepository>.Instance);

    private void SetupStats(string playerId, params PlayerStatEntity[] rows)
    {
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                Ez.Handball.Infrastructure.Tables.PlayerStats, $"RowKey eq '{playerId}'", default))
              .Returns(ToAsync(rows));
    }

    private void SetupTournaments(string season, params TournamentEntity[] rows)
    {
        _query.Setup(q => q.QueryAsync<TournamentEntity>(
                Ez.Handball.Infrastructure.Tables.Tournaments, $"PartitionKey eq '{season}'", default))
              .Returns(ToAsync(rows));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetByPlayerAsync_NoStats_ReturnsEmpty_DoesNotQueryTournaments()
    {
        SetupStats("nope");

        var result = await CreateSut().GetByPlayerAsync("nope", default);

        Assert.Empty(result);
        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByPlayerAsync_OneStat_MapsAllFields()
    {
        SetupStats("12345", new PlayerStatEntity
        {
            PartitionKey = "match-1",
            RowKey = "12345",
            Goals = 5, YellowCards = 0, TwoMinuteSuspensions = 1, RedCards = 0,
            TournamentId = "8444", Season = "2025-26",
            TeamId = "385-karlar", ClubName = "Stjarnan"
        });
        SetupTournaments("2025-26", new TournamentEntity
        {
            PartitionKey = "2025-26", RowKey = "8444",
            Name = "Olís deild karla", Gender = "karlar", Division = "1", Ingest = true, Priority = 10
        });

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        var only = Assert.Single(result);
        Assert.Equal("match-1", only.MatchId);
        Assert.Equal("8444", only.TournamentId);
        Assert.Equal("Olís deild karla", only.TournamentName);
        Assert.Equal("2025-26", only.Season);
        Assert.Equal("385-karlar", only.TeamId);
        Assert.Equal("Stjarnan", only.ClubName);
        Assert.Equal(5, only.Goals);
        Assert.Equal(1, only.TwoMinuteSuspensions);
    }

    [Fact]
    public async Task GetByPlayerAsync_MultiSeason_QueriesTournamentsOncePerSeason()
    {
        SetupStats("12345",
            new PlayerStatEntity { PartitionKey = "m1", RowKey = "12345", TournamentId = "8444", Season = "2025-26", TeamId = "385-karlar" },
            new PlayerStatEntity { PartitionKey = "m2", RowKey = "12345", TournamentId = "8444", Season = "2024-25", TeamId = "385-karlar" });
        SetupTournaments("2025-26", new TournamentEntity { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís 25" });
        SetupTournaments("2024-25", new TournamentEntity { PartitionKey = "2024-25", RowKey = "8444", Name = "Olís 24" });

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Equal(2, result.Count);
        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments, "PartitionKey eq '2025-26'", default), Times.Once);
        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments, "PartitionKey eq '2024-25'", default), Times.Once);
    }

    [Fact]
    public async Task GetByPlayerAsync_MissingTournament_TournamentNameIsNull()
    {
        SetupStats("12345", new PlayerStatEntity
        {
            PartitionKey = "m1", RowKey = "12345",
            TournamentId = "9999", Season = "2025-26", TeamId = "385-karlar"
        });
        SetupTournaments("2025-26");  // no rows

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Single(result);
        Assert.Null(result[0].TournamentName);
    }
}
