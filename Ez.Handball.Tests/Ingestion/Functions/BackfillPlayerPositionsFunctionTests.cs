using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class BackfillPlayerPositionsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();
    private readonly Mock<IBlobArchiver> _blobArchiver = new();

    private BackfillPlayerPositionsFunction CreateSut() => new(_tableWriter.Object, _blobArchiver.Object);

    private static DateTimeOffset Day(int d) => new(2026, 1, d, 0, 0, 0, TimeSpan.Zero);

    private const string GameJsonLeftBack = """
    {
      "players": {
        "home": [ { "player_id": 803, "name": "Arnór Snær Óskarsson", "number": 6, "position": "Left Back" } ],
        "away": []
      }
    }
    """;

    private void SetupOneArchivedMatch(string matchId, DateTimeOffset date, string json)
    {
        _blobArchiver.Setup(b => b.ListAsync("hbstatz/matches/", It.IsAny<CancellationToken>()))
            .Returns(ToAsync(new[] { $"hbstatz/matches/{matchId}.json" }));
        _blobArchiver.Setup(b => b.ReadAsync($"hbstatz/matches/{matchId}.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        _tableWriter.Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchEntity> { new() { PartitionKey = "9142", RowKey = matchId, HomeTeamId = "385-karlar", AwayTeamId = "390-karlar", Date = date } });
    }

    private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ProcessAsync_DryRun_ReportsChangeWithoutWriting()
    {
        SetupOneArchivedMatch("103414", Day(1), GameJsonLeftBack);
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", JerseyNumber = "6", Position = "" } });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '390-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", Position = "" } });

        var result = await CreateSut().ProcessAsync(dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.BlobsProcessed);
        // A dry run previews changes but reports zero as actually applied.
        Assert.Equal(0, result.PlayersUpdated);
        var change = Assert.Single(result.Changes);
        Assert.Equal("hsi-1", change.PlayerId);
        Assert.Equal("LB", change.NewPosition);
        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
        _tableWriter.Verify(t => t.UpsertAsync("PlayerPositionObservations", It.IsAny<PlayerPositionObservationEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_LiveRun_WritesObservationAndUpdatesPlayer()
    {
        SetupOneArchivedMatch("103414", Day(1), GameJsonLeftBack);
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", JerseyNumber = "6", Position = "" } });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '390-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", Position = "" } });

        var result = await CreateSut().ProcessAsync(dryRun: false);

        Assert.False(result.DryRun);
        Assert.Equal(1, result.PlayersUpdated);
        _tableWriter.Verify(t => t.UpsertAsync("PlayerPositionObservations",
            It.Is<PlayerPositionObservationEntity>(e => e.PartitionKey == "hsi-1" && e.RowKey == "103414" && e.Position == "LB"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "hsi-1" && e.Position == "LB"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NoMatchesRowForBlob_RecordsErrorAndContinues()
    {
        _blobArchiver.Setup(b => b.ListAsync("hbstatz/matches/", It.IsAny<CancellationToken>()))
            .Returns(ToAsync(new[] { "hbstatz/matches/999999.json" }));
        _tableWriter.Setup(t => t.QueryAsync<MatchEntity>("Matches", "RowKey eq '999999'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchEntity>());

        var result = await CreateSut().ProcessAsync(dryRun: true);

        Assert.Equal(0, result.BlobsProcessed);
        Assert.Single(result.Errors);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task ProcessAsync_UnreconcilablePlayer_IsReportedInErrorsNotSilentlyDropped()
    {
        SetupOneArchivedMatch("103414", Day(1), GameJsonLeftBack);
        // Roster has nobody matching "Arnór Snær Óskarsson" / #6, so reconciliation fails.
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '390-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        var result = await CreateSut().ProcessAsync(dryRun: true);

        // The blob is still fully processed (it's not a blob-level failure)...
        Assert.Equal(1, result.BlobsProcessed);
        Assert.Empty(result.Changes);
        // ...but the unreconciled player is visible in Errors, not silently dropped.
        Assert.Single(result.Errors);
        Assert.Contains("103414", result.Errors[0]);
        Assert.Contains("Arnór", result.Errors[0]);
    }
}
