using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Functions;

public class BootstrapRetiredFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private BootstrapRetiredFunction CreateSut() => new(_tableWriter.Object);

    private void SetupTournaments(params string[] seasons) =>
        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seasons.Select(s => new TournamentEntity { PartitionKey = s, RowKey = "8444" }).ToList());

    private void SetupStatsForSeason(string season, params string[] playerIds) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerStatEntity>("PlayerStats", $"Season eq '{season}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playerIds.Select(id => new PlayerStatEntity { PartitionKey = "m1", RowKey = id, Season = season }).ToList());

    private void SetupPlayers(params PlayerEntity[] players) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerEntity>("Players", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

    private static PlayerEntity Plr(string id) =>
        new() { PartitionKey = "385-karlar", RowKey = id, Name = $"P{id}", Position = "CB", Gender = "karlar", ClubId = "385" };

    [Fact]
    public async Task Process_MarksOnlyPlayersWithNoStatsInLatestSeason()
    {
        // 2025-26 is the lexical-max season; only "active" played it.
        SetupTournaments("2024-25", "2025-26");
        SetupStatsForSeason("2025-26", "active");
        SetupPlayers(Plr("active"), Plr("retiree"));

        var result = await CreateSut().ProcessAsync();

        Assert.Equal("2025-26", result.Season);
        Assert.Equal(1, result.Marked);

        // "retiree" is marked true; the full entity is preserved (Name not blanked).
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "retiree" && e.Retired == true && e.Name == "Pretiree"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);

        // "active" is never written.
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "active"),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task Process_OnlyEverSetsTrue_NeverWritesFalse()
    {
        SetupTournaments("2025-26");
        SetupStatsForSeason("2025-26", "active");
        SetupPlayers(Plr("active"), Plr("retiree"));

        await CreateSut().ProcessAsync();

        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.Retired == false),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task Process_AllPlayersHaveStats_MarksNothing()
    {
        SetupTournaments("2025-26");
        SetupStatsForSeason("2025-26", "active");
        SetupPlayers(Plr("active"));

        var result = await CreateSut().ProcessAsync();

        Assert.Equal("2025-26", result.Season);
        Assert.Equal(0, result.Marked);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task Process_NoTournaments_MarksNothing()
    {
        SetupTournaments();
        SetupPlayers(Plr("active"));

        var result = await CreateSut().ProcessAsync();

        Assert.Equal(string.Empty, result.Season);
        Assert.Equal(0, result.Marked);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }
}
