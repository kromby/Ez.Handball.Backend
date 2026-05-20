using System.Text.Json;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Functions;

public class FetchMatchDetailsFunctionTests
{
    private readonly Mock<IHsiApiClient> _apiClient = new();
    private readonly Mock<IBlobArchiver> _blobArchiver = new();

    private FetchMatchDetailsFunction CreateSut() =>
        new(_apiClient.Object, _blobArchiver.Object);

    private static string Serialize(MatchListResponse response) =>
        JsonSerializer.Serialize(response);

    [Fact]
    public async Task ProcessAsync_FetchesAndArchivesDetailsAndPlayerStats_WhenMatchIsOpen()
    {
        var match = new MatchSummary
        {
            GameId = "1001",
            HomeTeamId = "10",
            AwayTeamId = "20",
            Status = "O"
        };
        var blobContent = Serialize(new MatchListResponse { Data = [match] });

        _blobArchiver.Setup(b => b.ExistsAsync("raw/matches/1001/details.json", default))
            .ReturnsAsync(false);
        _apiClient.Setup(a => a.GetMatchDetailsJsonAsync("1001", default))
            .ReturnsAsync("""{"matchId":"1001"}""");
        _apiClient.Setup(a => a.GetMatchPlayerStatsJsonAsync("1001", "10", default))
            .ReturnsAsync("""{"clubId":"10"}""");
        _apiClient.Setup(a => a.GetMatchPlayerStatsJsonAsync("1001", "20", default))
            .ReturnsAsync("""{"clubId":"20"}""");

        await CreateSut().ProcessAsync(blobContent);

        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/1001/details.json", It.IsAny<string>(), default), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/1001/players-10.json", It.IsAny<string>(), default), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/1001/players-20.json", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SkipsMatch_WhenFinishedAndBlobAlreadyExists()
    {
        var match = new MatchSummary
        {
            GameId = "2001",
            HomeTeamId = "10",
            AwayTeamId = "20",
            Status = "S"
        };
        var blobContent = Serialize(new MatchListResponse { Data = [match] });

        _blobArchiver.Setup(b => b.ExistsAsync("raw/matches/2001/details.json", default))
            .ReturnsAsync(true);

        await CreateSut().ProcessAsync(blobContent);

        _apiClient.Verify(a => a.GetMatchDetailsJsonAsync(It.IsAny<string>(), default), Times.Never);
        _apiClient.Verify(a => a.GetMatchPlayerStatsJsonAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        _blobArchiver.Verify(b => b.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_FetchesMatch_WhenFinishedButBlobDoesNotExist()
    {
        var match = new MatchSummary
        {
            GameId = "3001",
            HomeTeamId = "10",
            AwayTeamId = "20",
            Status = "S"
        };
        var blobContent = Serialize(new MatchListResponse { Data = [match] });

        _blobArchiver.Setup(b => b.ExistsAsync("raw/matches/3001/details.json", default))
            .ReturnsAsync(false);
        _apiClient.Setup(a => a.GetMatchDetailsJsonAsync("3001", default))
            .ReturnsAsync("""{"matchId":"3001"}""");
        _apiClient.Setup(a => a.GetMatchPlayerStatsJsonAsync("3001", "10", default))
            .ReturnsAsync("""{"clubId":"10"}""");
        _apiClient.Setup(a => a.GetMatchPlayerStatsJsonAsync("3001", "20", default))
            .ReturnsAsync("""{"clubId":"20"}""");

        await CreateSut().ProcessAsync(blobContent);

        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/3001/details.json", It.IsAny<string>(), default), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/3001/players-10.json", It.IsAny<string>(), default), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/3001/players-20.json", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ContinuesWithRemainingMatches_WhenOneMatchThrows()
    {
        var matchFailing = new MatchSummary
        {
            GameId = "4001",
            HomeTeamId = "10",
            AwayTeamId = "20",
            Status = "O"
        };
        var matchSucceeding = new MatchSummary
        {
            GameId = "4002",
            HomeTeamId = "30",
            AwayTeamId = "40",
            Status = "O"
        };
        var blobContent = Serialize(new MatchListResponse { Data = [matchFailing, matchSucceeding] });

        _blobArchiver.Setup(b => b.ExistsAsync("raw/matches/4001/details.json", default))
            .ReturnsAsync(false);
        _blobArchiver.Setup(b => b.ExistsAsync("raw/matches/4002/details.json", default))
            .ReturnsAsync(false);

        _apiClient.Setup(a => a.GetMatchDetailsJsonAsync("4001", default))
            .ThrowsAsync(new HttpRequestException("timeout"));

        _apiClient.Setup(a => a.GetMatchDetailsJsonAsync("4002", default))
            .ReturnsAsync("""{"matchId":"4002"}""");
        _apiClient.Setup(a => a.GetMatchPlayerStatsJsonAsync("4002", "30", default))
            .ReturnsAsync("""{"clubId":"30"}""");
        _apiClient.Setup(a => a.GetMatchPlayerStatsJsonAsync("4002", "40", default))
            .ReturnsAsync("""{"clubId":"40"}""");

        await CreateSut().ProcessAsync(blobContent);

        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/4001/details.json", It.IsAny<string>(), default), Times.Never);
        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/4002/details.json", It.IsAny<string>(), default), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/4002/players-30.json", It.IsAny<string>(), default), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync("raw/matches/4002/players-40.json", It.IsAny<string>(), default), Times.Once);
    }
}
