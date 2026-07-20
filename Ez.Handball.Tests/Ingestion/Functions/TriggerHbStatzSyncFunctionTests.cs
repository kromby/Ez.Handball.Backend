using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class TriggerHbStatzSyncFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();
    private readonly Mock<QueueServiceClient> _queueServiceClient = new();
    private readonly Mock<QueueClient> _queueClient = new();

    public TriggerHbStatzSyncFunctionTests()
    {
        _queueServiceClient.Setup(q => q.GetQueueClient("hbstatz-match-sync")).Returns(_queueClient.Object);
    }

    private TriggerHbStatzSyncFunction CreateSut() =>
        new(_tableWriter.Object, _queueServiceClient.Object);

    [Fact]
    public async Task ProcessAsync_HappyPath_EnqueuesFinishedMatches()
    {
        var tournaments = new List<TournamentEntity>
        {
            new() { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís deild karla", IngestHbStatz = true }
        };

        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", It.Is<string>(f => f.Contains("IngestHbStatz eq true")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tournaments);

        var matches = new List<MatchEntity>
        {
            new() { PartitionKey = "8444", RowKey = "12922", Status = "S" },
            new() { PartitionKey = "8444", RowKey = "12923", Status = "S" }
        };

        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", It.Is<string>(f => f.Contains("PartitionKey eq '8444'")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var result = await CreateSut().ProcessAsync(null, null, null, CancellationToken.None);

        Assert.Equal(2, result);
        _queueClient.Verify(q => q.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_WithMatchId_EnqueuesSingleMatch()
    {
        var matches = new List<MatchEntity>
        {
            new() { PartitionKey = "8444", RowKey = "12922", Status = "S" }
        };

        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", "RowKey eq '12922'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var result = await CreateSut().ProcessAsync(null, null, "12922", CancellationToken.None);

        Assert.Equal(1, result);
        _queueClient.Verify(q => q.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
