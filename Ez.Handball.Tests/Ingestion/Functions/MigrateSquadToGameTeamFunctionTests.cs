using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class MigrateSquadToGameTeamFunctionTests
{
    private readonly Mock<ITableWriter> _w = new();

    private MigrateSquadToGameTeamFunction Sut() => new(_w.Object);

    private void StartingCap(double cap) => _w
        .Setup(w => w.GetAsync<ConfigEntity>("Config", "fantasy-squad-v1", "startingCap", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ConfigEntity { PartitionKey = "fantasy-squad-v1", RowKey = "startingCap", Value = cap.ToString(System.Globalization.CultureInfo.InvariantCulture) });

    private void Users(params string[] ids) => _w
        .Setup(w => w.QueryAsync<UserEntity>("Users", It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(ids.Select(id => new UserEntity { RowKey = id }).ToList());

    private void Squads(params SquadEntryEntity[] rows) => _w
        .Setup(w => w.QueryAsync<SquadEntryEntity>("Squads", It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(rows.ToList());

    private void NoExistingTeam() => _w
        .Setup(w => w.GetAsync<GameTeamEntity>("GameTeams", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((GameTeamEntity?)null);

    [Fact]
    public async Task Migrates_ActiveRows_To_Roster_And_PreservesRemainingBudget()
    {
        StartingCap(100_000_000); Users("u-1"); NoExistingTeam();
        Squads(
            new SquadEntryEntity { PartitionKey = "u-1", RowKey = "p-1", Position = "VS", PricePaidAmount = 40_000_000, DeletedAt = null },
            new SquadEntryEntity { PartitionKey = "u-1", RowKey = "p-2", Position = "MM", PricePaidAmount = 30_000_000, DeletedAt = null });

        var migrated = await Sut().ProcessAsync(default);

        Assert.Equal(1, migrated);
        _w.Verify(w => w.UpsertAsync("GameRosters", It.Is<GameRosterEntity>(r => r.PartitionKey == "u-1:fantasy" && r.RowKey == "p-1" && r.PricePaidAmount == 40_000_000), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Once);
        _w.Verify(w => w.UpsertAsync("GameTeams", It.Is<GameTeamEntity>(t => t.PartitionKey == "u-1" && t.RowKey == "fantasy" && t.TeamId == "u-1:fantasy"), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Once);
        _w.Verify(w => w.UpsertAsync("GameBudgets", It.Is<GameBudgetEntity>(b => b.PartitionKey == "u-1:fantasy" && b.Amount == 30_000_000), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Once);
    }

    [Fact]
    public async Task UserWithNoSquad_GetsTeam_AtStartingCap()
    {
        StartingCap(100_000_000); Users("u-2"); NoExistingTeam(); Squads(/* none */);

        await Sut().ProcessAsync(default);

        _w.Verify(w => w.UpsertAsync("GameBudgets", It.Is<GameBudgetEntity>(b => b.PartitionKey == "u-2:fantasy" && b.Amount == 100_000_000), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Once);
    }

    [Fact]
    public async Task SkipsUsers_ThatAlreadyHaveTeam()
    {
        StartingCap(100_000_000); Users("u-1");
        _w.Setup(w => w.GetAsync<GameTeamEntity>("GameTeams", "u-1", "fantasy", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GameTeamEntity { PartitionKey = "u-1", RowKey = "fantasy", TeamId = "u-1:fantasy" });

        Assert.Equal(0, await Sut().ProcessAsync(default));

        _w.Verify(w => w.UpsertAsync("GameTeams", It.IsAny<GameTeamEntity>(), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Never);
        _w.Verify(w => w.UpsertAsync("GameRosters", It.IsAny<GameRosterEntity>(), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Never);
        _w.Verify(w => w.UpsertAsync("GameBudgets", It.IsAny<GameBudgetEntity>(), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task SoftDeletedSquadRows_AreCarriedOver_AndExcludedFromBudgetMath()
    {
        StartingCap(100_000_000); Users("u-1"); NoExistingTeam();
        Squads(
            new SquadEntryEntity { PartitionKey = "u-1", RowKey = "p-1", PricePaidAmount = 40_000_000, DeletedAt = null },
            new SquadEntryEntity { PartitionKey = "u-1", RowKey = "p-2", PricePaidAmount = 30_000_000, DeletedAt = DateTimeOffset.UnixEpoch });

        await Sut().ProcessAsync(default);

        _w.Verify(w => w.UpsertAsync("GameBudgets", It.Is<GameBudgetEntity>(b => b.Amount == 60_000_000), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Once);
        _w.Verify(w => w.UpsertAsync("GameRosters", It.Is<GameRosterEntity>(r => r.RowKey == "p-2" && r.DeletedAt != null), It.IsAny<CancellationToken>(), It.IsAny<Azure.Data.Tables.TableUpdateMode>()), Times.Once);
    }
}
