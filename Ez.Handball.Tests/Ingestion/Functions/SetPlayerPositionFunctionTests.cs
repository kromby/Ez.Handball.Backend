using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SetPlayerPositionFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SetPlayerPositionFunction CreateSut() => new(_tableWriter.Object);

    [Fact]
    public async Task ProcessAsync_ValidRequest_DryRun_DoesNotWrite()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Aron", Position = "" } });

        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("hsi-1", "LB", null) }, dryRun: true);

        Assert.Equal("DryRun", Assert.Single(result.Results).Status);
        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_LiveRun_SetsPositionAndSecondary()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Aron", Position = "" } });

        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("hsi-1", "LB", "CB") }, dryRun: false);

        Assert.Equal("Applied", Assert.Single(result.Results).Status);
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "hsi-1" && e.Position == "LB" && e.PositionSecondary == "CB"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_InvalidPositionCode_ReturnsInvalidWithoutWriting()
    {
        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("hsi-1", "GOALKEEPER", null) }, dryRun: false);

        Assert.Equal("InvalidPosition", Assert.Single(result.Results).Status);
        _tableWriter.Verify(t => t.QueryAsync<PlayerEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_PlayerNotFound_ReturnsPlayerNotFound()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'nope'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("nope", "LB", null) }, dryRun: false);

        Assert.Equal("PlayerNotFound", Assert.Single(result.Results).Status);
    }
}
