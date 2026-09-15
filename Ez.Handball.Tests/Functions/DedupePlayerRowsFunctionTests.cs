using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Functions;

public class DedupePlayerRowsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private DedupePlayerRowsFunction CreateSut() => new(_tableWriter.Object);

    private void SetupPlayers(params PlayerEntity[] players) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerEntity>("Players", null!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

    [Fact]
    public async Task ProcessAsync_DuplicateRows_KeepsMostRecentlyUpdated_DeletesTheRest()
    {
        var stale = new PlayerEntity
        {
            PartitionKey = "453-karlar", RowKey = "173798", Name = "Ísak Logi Einarsson",
            ClubName = "Valur", Position = "Leikmaður", Timestamp = DateTimeOffset.Parse("2026-08-31T18:02:24Z")
        };
        var current = new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "173798", Name = "Ísak Logi Einarsson",
            ClubName = "Stjarnan", Position = "CB", Timestamp = DateTimeOffset.Parse("2026-09-15T20:44:20Z")
        };
        SetupPlayers(stale, current);

        var result = await CreateSut().ProcessAsync(dryRun: false);

        Assert.Equal(1, result.PlayersWithDuplicates);
        Assert.Equal(1, result.RowsRemoved);
        var change = Assert.Single(result.Changes);
        Assert.Equal("173798", change.PlayerId);
        Assert.Equal("385-karlar", change.KeptPartitionKey);
        Assert.Equal(new[] { "453-karlar" }, change.RemovedPartitionKeys);

        _tableWriter.Verify(t => t.DeleteAsync("Players", "453-karlar", "173798", It.IsAny<CancellationToken>()), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync("Players", "385-karlar", "173798", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DryRun_ReportsChanges_ButDeletesNothing()
    {
        var stale = new PlayerEntity
        {
            PartitionKey = "453-karlar", RowKey = "173798", Name = "Ísak Logi Einarsson",
            Timestamp = DateTimeOffset.Parse("2026-08-31T18:02:24Z")
        };
        var current = new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "173798", Name = "Ísak Logi Einarsson",
            Timestamp = DateTimeOffset.Parse("2026-09-15T20:44:20Z")
        };
        SetupPlayers(stale, current);

        var result = await CreateSut().ProcessAsync(dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.RowsRemoved);
        _tableWriter.Verify(t => t.DeleteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NoDuplicates_ReportsNothing_DeletesNothing()
    {
        SetupPlayers(new PlayerEntity { PartitionKey = "385-karlar", RowKey = "1", Name = "Solo Player" });

        var result = await CreateSut().ProcessAsync(dryRun: false);

        Assert.Equal(0, result.PlayersWithDuplicates);
        Assert.Equal(0, result.RowsRemoved);
        Assert.Empty(result.Changes);
        _tableWriter.Verify(t => t.DeleteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NewestRowIsPlaceholder_InheritsRealPositionFromStaleRow_BeforeDeletingIt()
    {
        // The newest row is the player's current club, but HBStatz hasn't enriched it yet, so it
        // still carries hsi.is's "Leikmaður" placeholder. A stale row from before the transfer
        // already has a real, HBStatz-derived position — that must survive the dedupe.
        var newestButPlaceholder = new PlayerEntity
        {
            PartitionKey = "453-karlar", RowKey = "180122", Name = "Allan Norðberg",
            ClubName = "Valur", Position = "Leikmaður", Timestamp = DateTimeOffset.Parse("2026-09-15T19:42:19Z")
        };
        var staleButReal = new PlayerEntity
        {
            PartitionKey = "221-karlar", RowKey = "180122", Name = "Allan Norðberg",
            ClubName = "KA", Position = "RW", Timestamp = DateTimeOffset.Parse("2026-09-05T21:31:53Z")
        };
        SetupPlayers(newestButPlaceholder, staleButReal);

        var result = await CreateSut().ProcessAsync(dryRun: false);

        var change = Assert.Single(result.Changes);
        Assert.Equal("453-karlar", change.KeptPartitionKey);

        // The surviving (newest-club) row is updated to carry the real position forward.
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "180122" && e.PartitionKey == "453-karlar" && e.Position == "RW"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);

        // The stale row is still deleted once its position has been carried forward.
        _tableWriter.Verify(t => t.DeleteAsync("Players", "221-karlar", "180122", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DryRun_NewestRowIsPlaceholder_DoesNotUpsertOrDelete()
    {
        var newestButPlaceholder = new PlayerEntity
        {
            PartitionKey = "453-karlar", RowKey = "180122", Name = "Allan Norðberg",
            Position = "Leikmaður", Timestamp = DateTimeOffset.Parse("2026-09-15T19:42:19Z")
        };
        var staleButReal = new PlayerEntity
        {
            PartitionKey = "221-karlar", RowKey = "180122", Name = "Allan Norðberg",
            Position = "RW", Timestamp = DateTimeOffset.Parse("2026-09-05T21:31:53Z")
        };
        SetupPlayers(newestButPlaceholder, staleButReal);

        var result = await CreateSut().ProcessAsync(dryRun: true);

        Assert.Equal(1, result.RowsRemoved);
        _tableWriter.Verify(t => t.UpsertAsync(
            It.IsAny<string>(), It.IsAny<PlayerEntity>(), It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()),
            Times.Never);
        _tableWriter.Verify(t => t.DeleteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ThreeDuplicateRows_KeepsNewestOnly_DeletesOtherTwo()
    {
        var oldest = new PlayerEntity
        {
            PartitionKey = "214-karlar", RowKey = "167016", Name = "Jóel Bernburg",
            Timestamp = DateTimeOffset.Parse("2026-08-31T18:07:41Z")
        };
        var middle = new PlayerEntity
        {
            PartitionKey = "127-karlar", RowKey = "167016", Name = "Jóel Bernburg",
            Timestamp = DateTimeOffset.Parse("2026-09-05T21:31:52Z")
        };
        var newest = new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "167016", Name = "Jóel Bernburg",
            Timestamp = DateTimeOffset.Parse("2026-09-15T19:42:21Z")
        };
        SetupPlayers(oldest, middle, newest);

        var result = await CreateSut().ProcessAsync(dryRun: false);

        Assert.Equal(2, result.RowsRemoved);
        var change = Assert.Single(result.Changes);
        Assert.Equal("385-karlar", change.KeptPartitionKey);
        Assert.Equal(new[] { "127-karlar", "214-karlar" }, change.RemovedPartitionKeys);

        _tableWriter.Verify(t => t.DeleteAsync("Players", "214-karlar", "167016", It.IsAny<CancellationToken>()), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync("Players", "127-karlar", "167016", It.IsAny<CancellationToken>()), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync("Players", "385-karlar", "167016", It.IsAny<CancellationToken>()), Times.Never);
    }
}
