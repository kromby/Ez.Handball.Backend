using Azure.Data.Tables;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class HbStatzPlayerPositionAggregatorTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private HbStatzPlayerPositionAggregator CreateSut() => new(_tableWriter.Object);

    private static DateTimeOffset Day(int d) => new(2026, 1, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAndRecomputeAsync_RecordsObservationRow()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "hsi-1", RowKey = "m1", Position = "LB", MatchDate = Day(1) }
            });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Position = "" } });

        await CreateSut().RecordAndRecomputeAsync("hsi-1", "m1", Day(1), "LB");

        _tableWriter.Verify(t => t.UpsertAsync("PlayerPositionObservations",
            It.Is<PlayerPositionObservationEntity>(e =>
                e.PartitionKey == "hsi-1" && e.RowKey == "m1" && e.Position == "LB" && e.MatchDate == Day(1)),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
    }

    [Fact]
    public async Task RecordAndRecomputeAsync_UpdatesPlayerPositionFromFullHistory()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "hsi-1", RowKey = "m1", Position = "LB", MatchDate = Day(1) },
                new() { PartitionKey = "hsi-1", RowKey = "m2", Position = "LB", MatchDate = Day(2) },
                new() { PartitionKey = "hsi-1", RowKey = "m3", Position = "CB", MatchDate = Day(3) },
            });
        var player = new PlayerEntity { PartitionKey = "385-karlar", RowKey = "hsi-1", Position = "" };
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { player });

        await CreateSut().RecordAndRecomputeAsync("hsi-1", "m3", Day(3), "CB");

        // Note: 3 total observations, 2 LB / 1 CB -> CB is 1/3 (~33%) of the total, which is
        // strictly above PositionModeCalculator's >10% secondary threshold, so CB is a legitimate
        // secondary here (not ""). The brief's original assertion expected PositionSecondary == "",
        // which is mathematically inconsistent with the already-implemented, already-tested
        // PositionModeCalculator.Compute (Task 2) — corrected to match its documented behavior.
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "hsi-1" && e.Position == "LB" && e.PositionSecondary == "CB"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task RecordAndRecomputeAsync_UnchangedPosition_DoesNotUpsertPlayer()
    {
        var player = new PlayerEntity { PartitionKey = "385-karlar", RowKey = "hsi-1", Position = "LB", PositionSecondary = "" };
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "hsi-1", RowKey = "m1", Position = "LB", MatchDate = Day(1) }
            });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { player });

        await CreateSut().RecordAndRecomputeAsync("hsi-1", "m1", Day(1), "LB");

        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task RecordAndRecomputeAsync_NoMatchingPlayerRow_StillRecordsObservationWithoutThrowing()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'ghost'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "ghost", RowKey = "m1", Position = "LB", MatchDate = Day(1) }
            });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'ghost'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        await CreateSut().RecordAndRecomputeAsync("ghost", "m1", Day(1), "LB");

        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }
}
