using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

[Collection("Azurite")]
public class TablePlayerRepositorySetPositionTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);
    private IPlayerRepository _sut = null!;

    public async Task InitializeAsync()
    {
        _sut = new TablePlayerRepository(
            _client, new TableQuery(_client),
            () => new DateOnly(2026, 5, 22),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TablePlayerRepository>.Instance);
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.Players).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
        => await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.Players).DeleteAsync();

    private async Task SeedAsync(PlayerEntity row) =>
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.Players)
            .UpsertEntityAsync(row, TableUpdateMode.Replace);

    [Fact]
    public async Task SetPositionAsync_ExistingPlayer_UpdatesPositionAndSecondary()
    {
        await SeedAsync(new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "1", Name = "X",
            Gender = "karlar", ClubId = "385", Position = "Leikmaður"
        });

        var updated = await _sut.SetPositionAsync("1", "CB", "LB", default);

        Assert.True(updated);
        var player = await _sut.GetByIdAsync("1", default);
        Assert.Equal("CB", player!.Position);
        Assert.Equal("LB", player.PositionSecondary);
    }

    [Fact]
    public async Task SetPositionAsync_PreservesOtherFields()
    {
        await SeedAsync(new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "1", Name = "Aron",
            Gender = "karlar", ClubId = "385", ClubName = "Stjarnan", Position = ""
        });

        await _sut.SetPositionAsync("1", "GK", "", default);

        var player = await _sut.GetByIdAsync("1", default);
        Assert.Equal("Aron", player!.Name);
        Assert.Equal("Stjarnan", player.ClubName);
    }

    [Fact]
    public async Task SetPositionAsync_UnknownPlayer_ReturnsFalse()
    {
        Assert.False(await _sut.SetPositionAsync("nope", "GK", "", default));
    }
}
