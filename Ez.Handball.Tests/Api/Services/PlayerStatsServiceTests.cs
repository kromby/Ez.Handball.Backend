using Ez.Handball.Api;
using Ez.Handball.Api.Models;
using Ez.Handball.Api.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Api.Services;

public class PlayerStatsServiceTests
{
    private readonly Mock<ITableQuery> _query = new();

    private PlayerStatsService CreateSut() =>
        new(_query.Object, NullLogger<PlayerStatsService>.Instance);

    private void SetupStatRows(string playerId, params PlayerStatEntity[] rows)
    {
        _query
            .Setup(q => q.QueryAsync<PlayerStatEntity>(
                Tables.PlayerStats, $"RowKey eq '{playerId}'", default))
            .Returns(ToAsync(rows));
    }

    private void SetupTournamentRows(string season, params TournamentEntity[] rows)
    {
        _query
            .Setup(q => q.QueryAsync<TournamentEntity>(
                Tables.Tournaments, $"PartitionKey eq '{season}'", default))
            .Returns(ToAsync(rows));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetStatsAsync_HappyPath_FillsTournamentNamesFromTournamentsTable()
    {
        const string playerId = "12345";

        SetupStatRows(playerId,
            new PlayerStatEntity
            {
                PartitionKey = "103414", RowKey = playerId,
                Goals = 5, YellowCards = 0, TwoMinuteSuspensions = 1, RedCards = 0,
                TournamentId = "8444", Season = "2025"
            },
            new PlayerStatEntity
            {
                PartitionKey = "103415", RowKey = playerId,
                Goals = 3, YellowCards = 1, TwoMinuteSuspensions = 0, RedCards = 0,
                TournamentId = "8437", Season = "2025"
            });

        SetupTournamentRows("2025",
            new TournamentEntity { PartitionKey = "2025", RowKey = "8444", Name = "Olís deild karla", Gender = "karlar" },
            new TournamentEntity { PartitionKey = "2025", RowKey = "8437", Name = "Powerade bikar karla", Gender = "karlar" });

        var sut = CreateSut();

        var rows = await sut.GetStatsAsync(playerId);

        Assert.Equal(2, rows.Count);

        var olis = Assert.Single(rows, r => r.MatchId == "103414");
        Assert.Equal("8444", olis.TournamentId);
        Assert.Equal("Olís deild karla", olis.TournamentName);
        Assert.Equal("2025", olis.Season);
        Assert.Equal(5, olis.Goals);

        var bikar = Assert.Single(rows, r => r.MatchId == "103415");
        Assert.Equal("Powerade bikar karla", bikar.TournamentName);
    }

    [Fact]
    public async Task GetStatsAsync_NoStatRows_ReturnsEmptyList_AndDoesNotQueryTournaments()
    {
        const string playerId = "12345";
        SetupStatRows(playerId);  // empty

        var sut = CreateSut();
        var rows = await sut.GetStatsAsync(playerId);

        Assert.Empty(rows);

        // The Tournaments query must NOT have been issued.
        _query.Verify(
            q => q.QueryAsync<TournamentEntity>(Tables.Tournaments, It.IsAny<string>(), default),
            Times.Never);
    }

    [Fact]
    public async Task GetStatsAsync_TournamentMissingFromTournamentsTable_LeavesNameNull()
    {
        const string playerId = "12345";

        SetupStatRows(playerId, new PlayerStatEntity
        {
            PartitionKey = "103414", RowKey = playerId,
            Goals = 5, YellowCards = 0, TwoMinuteSuspensions = 1, RedCards = 0,
            TournamentId = "9999", Season = "2025"
        });

        SetupTournamentRows("2025");  // no rows

        var sut = CreateSut();
        var rows = await sut.GetStatsAsync(playerId);

        var row = Assert.Single(rows);
        Assert.Equal("9999", row.TournamentId);
        Assert.Null(row.TournamentName);
        Assert.Equal("2025", row.Season);
    }

    [Fact]
    public async Task GetStatsAsync_MultipleSeasons_IssuesOneQueryPerSeason()
    {
        const string playerId = "12345";

        SetupStatRows(playerId,
            new PlayerStatEntity
            {
                PartitionKey = "1", RowKey = playerId,
                TournamentId = "8444", Season = "2025"
            },
            new PlayerStatEntity
            {
                PartitionKey = "2", RowKey = playerId,
                TournamentId = "8424", Season = "2024"
            });

        SetupTournamentRows("2025",
            new TournamentEntity { PartitionKey = "2025", RowKey = "8444", Name = "Olís deild karla", Gender = "karlar" });
        SetupTournamentRows("2024",
            new TournamentEntity { PartitionKey = "2024", RowKey = "8424", Name = "Grill 66 deild karla", Gender = "karlar" });

        var sut = CreateSut();
        var rows = await sut.GetStatsAsync(playerId);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Olís deild karla", rows.Single(r => r.Season == "2025").TournamentName);
        Assert.Equal("Grill 66 deild karla", rows.Single(r => r.Season == "2024").TournamentName);

        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Tables.Tournaments, "PartitionKey eq '2025'", default), Times.Once);
        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Tables.Tournaments, "PartitionKey eq '2024'", default), Times.Once);
    }
}
