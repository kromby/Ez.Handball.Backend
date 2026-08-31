using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class MergePlayersFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private MergePlayersFunction CreateSut() => new(_tableWriter.Object);

    private static PlayerEntity Plr(string clubId, string id, string name, DateTimeOffset? dob = null, string? jersey = null) =>
        new()
        {
            PartitionKey = $"{clubId}-karlar", RowKey = id, Name = name, Position = "Leikmaður",
            Gender = "karlar", ClubId = clubId, ClubName = "Stjarnan", DateOfBirth = dob, JerseyNumber = jersey
        };

    private void SetupPlayer(string id, PlayerEntity? entity) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerEntity>("Players", $"RowKey eq '{id}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity is null ? new List<PlayerEntity>() : [entity]);

    private void SetupStats(string playerId, params PlayerStatEntity[] stats) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerStatEntity>("PlayerStats", $"RowKey eq '{playerId}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats.ToList());

    private void SetupNoFantasyReferences(string playerId)
    {
        foreach (var table in new[] { "GameRosters", "Squads", "Shortlists", "GameLineups", "GameweekLineups" })
        {
            _tableWriter
                .Setup(t => t.QueryAsync<TableEntity>(table, $"RowKey eq '{playerId}'", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TableEntity>());
        }
        _tableWriter
            .Setup(t => t.QueryAsync<TableEntity>("GameTransferLedger", $"PlayerId eq '{playerId}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableEntity>());
        _tableWriter
            .Setup(t => t.QueryAsync<TableEntity>("GameweekScores", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableEntity>());
    }

    [Fact]
    public async Task Merge_MovesStatsAndBackfillsFields_DeletesLoserRow()
    {
        SetupPlayer("181704", Plr("385", "181704", "Jóhannes Bjorgvin"));
        SetupPlayer("181844", Plr("385", "181844", "Jóhannes Björgvin", dob: new DateTimeOffset(2000, 5, 25, 0, 0, 0, TimeSpan.Zero)));
        SetupNoFantasyReferences("181844");
        SetupStats("181844",
            new PlayerStatEntity { PartitionKey = "98630", RowKey = "181844", Goals = 3, Season = "2024-25" },
            new PlayerStatEntity { PartitionKey = "98640", RowKey = "181844", Goals = 2, Season = "2024-25" });

        var result = await CreateSut().ProcessAsync(
            [new MergePlayersRequest("181704", "181844", "Jóhannes Björgvin")], dryRun: false);

        Assert.Equal("Applied", result.Results[0].Status);

        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.Is<PlayerStatEntity>(e => e.PartitionKey == "98630" && e.RowKey == "181704" && e.Goals == 3),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.Is<PlayerStatEntity>(e => e.PartitionKey == "98640" && e.RowKey == "181704" && e.Goals == 2),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync("PlayerStats", "98630", "181844", It.IsAny<CancellationToken>()), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync("PlayerStats", "98640", "181844", It.IsAny<CancellationToken>()), Times.Once);

        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "181704" && e.Name == "Jóhannes Björgvin" && e.DateOfBirth != null),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync("Players", "385-karlar", "181844", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Merge_DryRun_WritesNothing()
    {
        SetupPlayer("181704", Plr("385", "181704", "Jóhannes Bjorgvin"));
        SetupPlayer("181844", Plr("385", "181844", "Jóhannes Björgvin"));
        SetupNoFantasyReferences("181844");
        SetupStats("181844", new PlayerStatEntity { PartitionKey = "98630", RowKey = "181844" });

        var result = await CreateSut().ProcessAsync(
            [new MergePlayersRequest("181704", "181844", null)], dryRun: true);

        Assert.Equal("DryRun", result.Results[0].Status);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<ITableEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Merge_ReferencedInFantasyTable_BlocksAndWritesNothing()
    {
        SetupPlayer("181704", Plr("385", "181704", "Jóhannes Bjorgvin"));
        SetupPlayer("181844", Plr("385", "181844", "Jóhannes Björgvin"));

        _tableWriter
            .Setup(t => t.QueryAsync<TableEntity>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TableEntity>());
        _tableWriter
            .Setup(t => t.QueryAsync<TableEntity>("GameRosters", "RowKey eq '181844'", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TableEntity("team1", "181844")]);

        var result = await CreateSut().ProcessAsync(
            [new MergePlayersRequest("181704", "181844", null)], dryRun: false);

        Assert.Equal("BlockedReferencedElsewhere", result.Results[0].Status);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<ITableEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Merge_MergePlayerNotFound_ReportsWithoutWriting()
    {
        SetupPlayer("181704", Plr("385", "181704", "Jóhannes Bjorgvin"));
        SetupPlayer("181844", null);

        var result = await CreateSut().ProcessAsync(
            [new MergePlayersRequest("181704", "181844", null)], dryRun: false);

        Assert.Equal("MergePlayerNotFound", result.Results[0].Status);
    }

    [Fact]
    public async Task Merge_SameId_ReportsWithoutWriting()
    {
        var result = await CreateSut().ProcessAsync(
            [new MergePlayersRequest("181704", "181704", null)], dryRun: false);

        Assert.Equal("SameId", result.Results[0].Status);
    }
}
