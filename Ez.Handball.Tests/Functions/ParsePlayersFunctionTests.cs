using System.Text.Json;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Functions;

public class ParsePlayersFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private ParsePlayersFunction CreateSut() => new(_tableWriter.Object);

    private static string BuildPlayerStatsJson(IEnumerable<PlayerStatDto> players)
    {
        var response = new PlayerStatsResponse { Data = players.ToList() };
        return JsonSerializer.Serialize(response);
    }

    private static MatchEntity BuildMatch(
        string matchId = "5001",
        string tournamentId = "8444",
        string homeTeamId = "385-karlar",
        string awayTeamId = "390-karlar")
    {
        return new MatchEntity
        {
            PartitionKey = tournamentId,
            RowKey = matchId,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId
        };
    }

    [Fact]
    public async Task ProcessAsync_HappyPath_UpsertsPlayerAndPlayerStats()
    {
        // Arrange
        const string matchId = "5001";
        const string clubId = "385";
        const string teamId = "385-karlar";

        var match = BuildMatch(matchId: matchId, homeTeamId: teamId, awayTeamId: "390-karlar");
        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", default))
            .ReturnsAsync(new List<MatchEntity> { match });

        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8444'", default))
            .ReturnsAsync(new List<TournamentEntity>
            {
                new() { PartitionKey = "2025", RowKey = "8444", Name = "Olís deild karla", Gender = "karlar" }
            });

        _tableWriter
            .Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", clubId, default))
            .ReturnsAsync(new ClubEntity
            {
                PartitionKey = "club",
                RowKey = clubId,
                Name = "Stjarnan"
            });

        var player = new PlayerStatDto
        {
            PlayerId = "42",
            Name = "Jón Jónsson",
            Position = "Goalkeeper",
            Player = "1",
            Goals = "3",
            YellowCards = "1",
            RedCards = "0"
        };

        var blobContent = BuildPlayerStatsJson(new[] { player });

        // Act
        await CreateSut().ProcessAsync(blobContent, matchId, clubId);

        // Assert — PlayerEntity upsert
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e =>
                e.PartitionKey == teamId &&
                e.RowKey == "42" &&
                e.Name == "Jón Jónsson" &&
                e.Position == "Goalkeeper" &&
                e.Gender == "karlar" &&
                e.ClubId == "385" &&
                e.ClubName == "Stjarnan"),
            default), Times.Once);

        // Assert — PlayerStatEntity upsert
        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.Is<PlayerStatEntity>(e =>
                e.PartitionKey == matchId &&
                e.RowKey == "42" &&
                e.Goals == 3 &&
                e.YellowCards == 1 &&
                e.TwoMinuteSuspensions == 0 &&
                e.RedCards == 0 &&
                e.TournamentId == "8444" &&
                e.Season == "2025"),
            default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AwayClubResolution_UsesAwayTeamId()
    {
        // Arrange
        const string matchId = "5001";
        const string clubId = "390";
        const string homeTeamId = "385-karlar";
        const string awayTeamId = "390-karlar";

        var match = BuildMatch(matchId: matchId, homeTeamId: homeTeamId, awayTeamId: awayTeamId);
        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", default))
            .ReturnsAsync(new List<MatchEntity> { match });

        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8444'", default))
            .ReturnsAsync(new List<TournamentEntity>
            {
                new() { PartitionKey = "2025", RowKey = "8444", Name = "Olís deild karla", Gender = "karlar" }
            });

        _tableWriter
            .Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", clubId, default))
            .ReturnsAsync(new ClubEntity
            {
                PartitionKey = "club",
                RowKey = clubId,
                Name = "Breiðablik"
            });

        var player = new PlayerStatDto
        {
            PlayerId = "99",
            Name = "Anna Sigurðardóttir",
            Position = "Left Back",
            Player = "1",
            Goals = "5",
            YellowCards = "0",
            RedCards = "1"
        };

        var blobContent = BuildPlayerStatsJson(new[] { player });

        // Act
        await CreateSut().ProcessAsync(blobContent, matchId, clubId);

        // Assert — teamId resolves to away team
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e =>
                e.PartitionKey == awayTeamId &&
                e.RowKey == "99" &&
                e.Gender == "karlar" &&
                e.ClubId == "390" &&
                e.ClubName == "Breiðablik"),
            default), Times.Once);

        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.Is<PlayerStatEntity>(e =>
                e.PartitionKey == matchId &&
                e.RowKey == "99" &&
                e.Goals == 5 &&
                e.RedCards == 1 &&
                e.TournamentId == "8444" &&
                e.Season == "2025"),
            default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_StaffEntry_IsFiltered_NoUpserts()
    {
        // Arrange
        const string matchId = "5001";
        const string clubId = "385";

        var match = BuildMatch(matchId: matchId);
        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", default))
            .ReturnsAsync(new List<MatchEntity> { match });

        var staff = new PlayerStatDto
        {
            PlayerId = "10",
            Name = "Coach Person",
            Position = "Coach",
            Player = "0",   // staff — must be filtered out
            Goals = "0",
            YellowCards = "0",
            RedCards = "0"
        };

        var blobContent = BuildPlayerStatsJson(new[] { staff });

        // Act
        await CreateSut().ProcessAsync(blobContent, matchId, clubId);

        // Assert — nothing upserted
        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<PlayerEntity>(),
            default), Times.Never);

        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<PlayerStatEntity>(),
            default), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_MatchNotFound_NoUpserts()
    {
        // Arrange
        const string matchId = "9999";
        const string clubId = "385";

        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", default))
            .ReturnsAsync(new List<MatchEntity>());

        var player = new PlayerStatDto
        {
            PlayerId = "1",
            Name = "Some Player",
            Player = "1",
            Goals = "2"
        };

        var blobContent = BuildPlayerStatsJson(new[] { player });

        // Act
        await CreateSut().ProcessAsync(blobContent, matchId, clubId);

        // Assert — nothing upserted when match lookup fails
        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<PlayerEntity>(),
            default), Times.Never);

        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<PlayerStatEntity>(),
            default), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ClubLookupReturnsNull_WritesNullClubName_AndProceeds()
    {
        // Arrange
        const string matchId = "5001";
        const string clubId = "385";
        const string teamId = "385-karlar";

        var match = BuildMatch(matchId: matchId, homeTeamId: teamId, awayTeamId: "390-karlar");
        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", default))
            .ReturnsAsync(new List<MatchEntity> { match });

        // Club lookup returns null
        _tableWriter
            .Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", clubId, default))
            .ReturnsAsync((ClubEntity?)null);

        var player = new PlayerStatDto
        {
            PlayerId = "42",
            Name = "Jón Jónsson",
            Position = "Goalkeeper",
            Player = "1",
            Goals = "1"
        };

        var blobContent = BuildPlayerStatsJson(new[] { player });

        // Act — must not throw
        await CreateSut().ProcessAsync(blobContent, matchId, clubId);

        // Assert — PlayerEntity still written, ClubName is null
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e =>
                e.PartitionKey == teamId &&
                e.RowKey == "42" &&
                e.ClubName == null &&
                e.Gender == "karlar" &&
                e.ClubId == "385"),
            default), Times.Once);

        // PlayerStat write still happens
        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.IsAny<PlayerStatEntity>(),
            default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_TournamentLookupReturnsEmpty_WritesEmptySeason_AndProceeds()
    {
        // Arrange
        const string matchId = "5001";
        const string clubId = "385";
        const string teamId = "385-karlar";

        var match = BuildMatch(matchId: matchId, tournamentId: "8444", homeTeamId: teamId, awayTeamId: "390-karlar");
        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", default))
            .ReturnsAsync(new List<MatchEntity> { match });

        _tableWriter
            .Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", clubId, default))
            .ReturnsAsync(new ClubEntity { PartitionKey = "club", RowKey = clubId, Name = "Stjarnan" });

        // Tournaments lookup returns empty
        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8444'", default))
            .ReturnsAsync(new List<TournamentEntity>());

        var player = new PlayerStatDto
        {
            PlayerId = "42",
            Name = "Jón Jónsson",
            Position = "Goalkeeper",
            Player = "1",
            Goals = "1"
        };

        var blobContent = BuildPlayerStatsJson(new[] { player });

        // Act — must not throw
        await CreateSut().ProcessAsync(blobContent, matchId, clubId);

        // Assert — PlayerStat still written, TournamentId from match, Season empty
        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.Is<PlayerStatEntity>(e =>
                e.PartitionKey == matchId &&
                e.RowKey == "42" &&
                e.TournamentId == "8444" &&
                e.Season == string.Empty),
            default), Times.Once);

        // PlayerEntity still written
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.IsAny<PlayerEntity>(),
            default), Times.Once);
    }
}
