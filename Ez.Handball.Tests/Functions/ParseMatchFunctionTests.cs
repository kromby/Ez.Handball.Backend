using System.Text.Json;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Functions;

public class ParseMatchFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private ParseMatchFunction CreateSut() => new(_tableWriter.Object);

    private static string BuildMatchDetailsJson(
        string tournamentId = "8444",
        string clubHomeId = "385",
        string homeClubName = "KR",
        string clubGuestId = "390",
        string guestClubName = "Breiðablik",
        string reportStatus = "S",
        string gamesResultHome = "28",
        string gamesResultGuest = "25",
        string date = "03.09.2025 - 19:30")
    {
        var response = new MatchDetailsResponse
        {
            Data = new MatchDetailsData
            {
                TournamentId = tournamentId,
                ClubHomeId = clubHomeId,
                HomeClubName = homeClubName,
                ClubGuestId = clubGuestId,
                GuestClubName = guestClubName,
                ReportStatus = reportStatus,
                GamesResultHome = gamesResultHome,
                GamesResultGuest = gamesResultGuest,
                Date = date
            }
        };
        return JsonSerializer.Serialize(response);
    }

    [Fact]
    public async Task ProcessAsync_HappyPath_UpsertsClubsTeamsAndMatch()
    {
        var tournamentEntity = new TournamentEntity
        {
            PartitionKey = "2025",
            RowKey = "8444",
            Name = "Olís deild karla",
            Gender = "karlar",
            Division = "1"
        };

        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8444'", default))
            .ReturnsAsync(new List<TournamentEntity> { tournamentEntity });

        var blobContent = BuildMatchDetailsJson(
            tournamentId: "8444",
            clubHomeId: "385",
            homeClubName: "KR",
            clubGuestId: "390",
            guestClubName: "Breiðablik",
            reportStatus: "S",
            gamesResultHome: "28",
            gamesResultGuest: "25");

        await CreateSut().ProcessAsync(blobContent, "5001");

        // Clubs upserted
        _tableWriter.Verify(t => t.UpsertAsync("Clubs",
            It.Is<ClubEntity>(e => e.PartitionKey == "club" && e.RowKey == "385" && e.Name == "KR"),
            default), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("Clubs",
            It.Is<ClubEntity>(e => e.PartitionKey == "club" && e.RowKey == "390" && e.Name == "Breiðablik"),
            default), Times.Once);

        // Teams upserted
        _tableWriter.Verify(t => t.UpsertAsync("Teams",
            It.Is<TeamEntity>(e =>
                e.PartitionKey == "team" &&
                e.RowKey == "385-karlar" &&
                e.ClubId == "385" &&
                e.Gender == "karlar" &&
                e.Name == "KR"),
            default), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("Teams",
            It.Is<TeamEntity>(e =>
                e.PartitionKey == "team" &&
                e.RowKey == "390-karlar" &&
                e.ClubId == "390" &&
                e.Gender == "karlar" &&
                e.Name == "Breiðablik"),
            default), Times.Once);

        // Match upserted
        _tableWriter.Verify(t => t.UpsertAsync("Matches",
            It.Is<MatchEntity>(e =>
                e.PartitionKey == "8444" &&
                e.RowKey == "5001" &&
                e.HomeTeamId == "385-karlar" &&
                e.AwayTeamId == "390-karlar" &&
                e.HomeScore == 28 &&
                e.AwayScore == 25 &&
                e.Status == "S"),
            default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_KvennaTournament_UsesKvennaSuffix()
    {
        var tournamentEntity = new TournamentEntity
        {
            PartitionKey = "2025",
            RowKey = "8434",
            Name = "Olís deild kvenna",
            Gender = "kvenna",
            Division = "1"
        };

        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8434'", default))
            .ReturnsAsync(new List<TournamentEntity> { tournamentEntity });

        var blobContent = BuildMatchDetailsJson(
            tournamentId: "8434",
            clubHomeId: "385",
            clubGuestId: "390");

        await CreateSut().ProcessAsync(blobContent, "6001");

        _tableWriter.Verify(t => t.UpsertAsync("Teams",
            It.Is<TeamEntity>(e => e.RowKey == "385-kvenna" && e.Gender == "kvenna"),
            default), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("Teams",
            It.Is<TeamEntity>(e => e.RowKey == "390-kvenna" && e.Gender == "kvenna"),
            default), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("Matches",
            It.Is<MatchEntity>(e => e.HomeTeamId == "385-kvenna" && e.AwayTeamId == "390-kvenna"),
            default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ClubNames_AreCorrectlyPassedToClubEntity()
    {
        var tournamentEntity = new TournamentEntity
        {
            PartitionKey = "2025",
            RowKey = "8444",
            Gender = "karlar"
        };

        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8444'", default))
            .ReturnsAsync(new List<TournamentEntity> { tournamentEntity });

        var blobContent = BuildMatchDetailsJson(
            tournamentId: "8444",
            clubHomeId: "100",
            homeClubName: "Stjarnan",
            clubGuestId: "200",
            guestClubName: "Haukar");

        await CreateSut().ProcessAsync(blobContent, "7001");

        _tableWriter.Verify(t => t.UpsertAsync("Clubs",
            It.Is<ClubEntity>(e => e.RowKey == "100" && e.Name == "Stjarnan"),
            default), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("Clubs",
            It.Is<ClubEntity>(e => e.RowKey == "200" && e.Name == "Haukar"),
            default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_TournamentNotFound_DoesNotUpsertAnyEntities()
    {
        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '9999'", default))
            .ReturnsAsync(new List<TournamentEntity>());

        var blobContent = BuildMatchDetailsJson(tournamentId: "9999");

        await CreateSut().ProcessAsync(blobContent, "8001");

        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<ClubEntity>(),
            default), Times.Never);
        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<TeamEntity>(),
            default), Times.Never);
        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<MatchEntity>(),
            default), Times.Never);
    }
}
