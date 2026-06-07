using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Functions;

public class FetchMatchListFunctionTests
{
    private readonly Mock<IHsiApiClient> _apiClient = new();
    private readonly Mock<IBlobArchiver> _blobArchiver = new();
    private readonly Mock<ITableWriter> _tableWriter = new();

    private FetchMatchListFunction CreateSut() =>
        new(_apiClient.Object, _blobArchiver.Object, _tableWriter.Object);

    [Fact]
    public async Task SyncAsync_ArchivesMatchListForEachTournament()
    {
        var tournaments = new List<TournamentEntity>
        {
            new() { PartitionKey = "2025", RowKey = "8444", Name = "Olís deild karla", Gender = "karlar", Division = "1" },
            new() { PartitionKey = "2025", RowKey = "8434", Name = "Olís deild kvenna", Gender = "kvenna", Division = "1" }
        };
        _tableWriter.Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "Ingest eq true", default))
            .ReturnsAsync(tournaments);
        _apiClient.Setup(a => a.GetTournamentMatchesJsonAsync("8444", default)).ReturnsAsync("""{"data":[]}""");
        _apiClient.Setup(a => a.GetTournamentMatchesJsonAsync("8434", default)).ReturnsAsync("""{"data":[]}""");

        var result = await CreateSut().SyncAsync();

        Assert.Equal(2, result.Synced);
        Assert.Empty(result.Failed);
        _blobArchiver.Verify(b => b.SaveAsync("tournaments/8444/matches.json", It.IsAny<string>(), default), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync("tournaments/8434/matches.json", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_ContinuesAndRecordsFailed_WhenOneTournamentThrows()
    {
        var tournaments = new List<TournamentEntity>
        {
            new() { PartitionKey = "2025", RowKey = "8444", Gender = "karlar", Division = "1" },
            new() { PartitionKey = "2025", RowKey = "8434", Gender = "kvenna", Division = "1" }
        };
        _tableWriter.Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "Ingest eq true", default))
            .ReturnsAsync(tournaments);
        _apiClient.Setup(a => a.GetTournamentMatchesJsonAsync("8444", default))
            .ThrowsAsync(new HttpRequestException("timeout"));
        _apiClient.Setup(a => a.GetTournamentMatchesJsonAsync("8434", default))
            .ReturnsAsync("""{"data":[]}""");

        var result = await CreateSut().SyncAsync();

        Assert.Equal(1, result.Synced);
        Assert.Equal(new[] { "8444" }, result.Failed);
    }

    [Fact]
    public async Task SyncAsync_ReturnsZero_WhenNoTournamentsInTable()
    {
        _tableWriter.Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "Ingest eq true", default))
            .ReturnsAsync(new List<TournamentEntity>());

        var result = await CreateSut().SyncAsync();

        Assert.Equal(0, result.Synced);
        Assert.Empty(result.Failed);
    }
}
