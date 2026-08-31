using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class TransferPlayersFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private TransferPlayersFunction CreateSut() => new(_tableWriter.Object);

    private void SetupClubs(params ClubEntity[] clubs) =>
        _tableWriter
            .Setup(t => t.QueryAsync<ClubEntity>("Clubs", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clubs.ToList());

    private void SetupPlayersForClub(string clubId, params PlayerEntity[] players) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerEntity>("Players", $"ClubId eq '{clubId}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

    private void SetupAllPlayers(params PlayerEntity[] players) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerEntity>("Players", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

    private static ClubEntity Club(string id, string name) => new() { RowKey = id, Name = name };

    private static PlayerEntity Plr(string clubId, string clubName, string id, string name, string gender = "karlar") =>
        new()
        {
            PartitionKey = $"{clubId}-{gender}", RowKey = id, Name = name, Position = "CB",
            Gender = gender, ClubId = clubId, ClubName = clubName
        };

    [Fact]
    public async Task Transfer_MovesPlayerToNewClub_UpsertsNewRowAndDeletesOld()
    {
        SetupClubs(Club("1", "FH"), Club("2", "HK"));
        SetupPlayersForClub("2", Plr("2", "HK", "p1", "Ágúst Guðmundsson"));

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Ágúst Guðmundsson", "HK", "FH", "transfer", null)], dryRun: false);

        Assert.Equal("Applied", result.Results[0].Status);

        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.PartitionKey == "1-karlar" && e.RowKey == "p1" && e.ClubId == "1" && e.ClubName == "FH"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);

        _tableWriter.Verify(t => t.UpsertAsync("Teams",
            It.Is<TeamEntity>(e => e.RowKey == "1-karlar" && e.ClubId == "1"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);

        _tableWriter.Verify(t => t.DeleteAsync("Players", "2-karlar", "p1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Transfer_DryRun_WritesNothing()
    {
        SetupClubs(Club("1", "FH"), Club("2", "HK"));
        SetupPlayersForClub("2", Plr("2", "HK", "p1", "Ágúst Guðmundsson"));

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Ágúst Guðmundsson", "HK", "FH", "transfer", null)], dryRun: true);

        Assert.Equal("DryRun", result.Results[0].Status);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<ITableEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Transfer_ToUnknownClub_CreatesClubOnTheFly()
    {
        SetupClubs(Club("2", "Fram"));
        SetupPlayersForClub("2", Plr("2", "Fram", "p2", "Breki Hrafn Árnason"));

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Breki Hrafn Árnason", "Fram", "Erlendis", "transfer", null)], dryRun: false);

        Assert.Equal("Applied", result.Results[0].Status);
        _tableWriter.Verify(t => t.UpsertAsync("Clubs",
            It.Is<ClubEntity>(c => c.Name == "Erlendis"), It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task Transfer_PlayerNotFoundInFromClub_ReportsNotFound()
    {
        SetupClubs(Club("1", "FH"), Club("2", "HK"));
        SetupPlayersForClub("2", Plr("2", "HK", "p1", "Someone Else"));

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Ágúst Guðmundsson", "HK", "FH", "transfer", null)], dryRun: false);

        Assert.Equal("PlayerNotFound", result.Results[0].Status);
    }

    [Fact]
    public async Task Retire_SetsRetiredTrueViaMerge_WithoutMovingClub()
    {
        SetupClubs(Club("3", "Selfoss"));
        SetupPlayersForClub("3", Plr("3", "Selfoss", "p3", "Sverrir Pálsson"));

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Sverrir Pálsson", "Selfoss", null, "retire", null)], dryRun: false);

        Assert.Equal("Applied", result.Results[0].Status);
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "p3" && e.Retired == true && e.PartitionKey == "3-karlar"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NewArrivalWithNoExistingRow_CreatesPlaceholder()
    {
        SetupClubs(Club("4", "Þór"));
        SetupPlayersForClub("4"); // no existing rows for this club

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Bertram Simonsen", null, "Þór", "create", null)], dryRun: false);

        Assert.Equal("Applied", result.Results[0].Status);
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.Name == "Bertram Simonsen" && e.ClubId == "4" && e.RowKey.StartsWith("placeholder-")),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
    }

    [Fact]
    public async Task Create_PlayerAlreadyExistsUnderClub_SkipsAsAlreadyExists()
    {
        SetupClubs(Club("4", "Þór"));
        SetupPlayersForClub("4", Plr("4", "Þór", "existing", "Bertram Simonsen"));

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Bertram Simonsen", null, "Þór", "create", null)], dryRun: false);

        Assert.Equal("AlreadyExists", result.Results[0].Status);
        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task UnknownAction_IsReportedWithoutThrowing()
    {
        SetupClubs();
        SetupAllPlayers();

        var result = await CreateSut().ProcessAsync(
            [new TransferRequest("Whoever", null, null, "bogus", null)], dryRun: false);

        Assert.Equal("UnknownAction", result.Results[0].Status);
    }
}
