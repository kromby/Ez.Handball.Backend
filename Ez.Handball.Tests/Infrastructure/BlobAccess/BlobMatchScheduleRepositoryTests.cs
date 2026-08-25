using Ez.Handball.Infrastructure.BlobAccess;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.BlobAccess;

public class BlobMatchScheduleRepositoryTests
{
    private readonly Mock<IBlobReader> _blobs = new();

    private BlobMatchScheduleRepository CreateSut() => new(_blobs.Object);

    private static readonly DateTimeOffset LastModified = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_BlobMissing_ReturnsNull()
    {
        _blobs.Setup(b => b.ReadAsync("tournaments/8444/matches.json", It.IsAny<CancellationToken>()))
              .ReturnsAsync((BlobContent?)null);

        var result = await CreateSut().GetAsync("8444", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ParsesMatchListJson_MappingKnownFields()
    {
        const string json = """
        {
          "data": [
            {
              "GameId": "103414",
              "Round": "1",
              "GameDayTime": "2025-09-03T19:30:00",
              "HomeTeamid": "385",
              "HomeTeamName": "Stjarnan",
              "AwayTeamId": "390",
              "AwayTeamName": "Breiðablik",
              "Status": "S",
              "ResultHomeTeam": "28",
              "ResultAwayTeam": "25",
              "StadiumName": "Ásgarður"
            }
          ]
        }
        """;
        _blobs.Setup(b => b.ReadAsync("tournaments/8444/matches.json", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new BlobContent(json, LastModified));

        var result = await CreateSut().GetAsync("8444", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(LastModified, result!.LastSyncedAt);
        var match = Assert.Single(result.Matches);
        Assert.Equal("103414", match.MatchId);
        Assert.Equal("1", match.Round);
        Assert.Equal(new DateTimeOffset(2025, 9, 3, 19, 30, 0, TimeSpan.Zero), match.Date);
        Assert.Equal("Stjarnan", match.HomeTeamName);
        Assert.Equal("Breiðablik", match.AwayTeamName);
        Assert.Equal("S", match.HsiStatus);
        Assert.Equal("Ásgarður", match.Venue);
    }

    [Fact]
    public async Task GetAsync_BlankStadiumName_MapsToNullVenue()
    {
        const string json = """
        { "data": [ { "GameId": "1", "Round": "1", "GameDayTime": "2025-09-03T19:30:00", "Status": "O", "StadiumName": "" } ] }
        """;
        _blobs.Setup(b => b.ReadAsync("tournaments/8444/matches.json", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new BlobContent(json, LastModified));

        var result = await CreateSut().GetAsync("8444", CancellationToken.None);

        Assert.Null(Assert.Single(result!.Matches).Venue);
    }

    [Fact]
    public async Task GetAsync_TrimsPaddedStadiumName()
    {
        const string json = """
        { "data": [ { "GameId": "1", "Round": "1", "GameDayTime": "2025-09-03T19:30:00", "Status": "O", "StadiumName": "Kórinn              " } ] }
        """;
        _blobs.Setup(b => b.ReadAsync("tournaments/8444/matches.json", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new BlobContent(json, LastModified));

        var result = await CreateSut().GetAsync("8444", CancellationToken.None);

        Assert.Equal("Kórinn", Assert.Single(result!.Matches).Venue);
    }

    [Fact]
    public async Task GetAsync_EmptyDataArray_ReturnsEmptyMatches()
    {
        _blobs.Setup(b => b.ReadAsync("tournaments/8444/matches.json", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new BlobContent("""{ "data": [] }""", LastModified));

        var result = await CreateSut().GetAsync("8444", CancellationToken.None);

        Assert.Empty(result!.Matches);
        Assert.Equal(LastModified, result.LastSyncedAt);
    }
}
