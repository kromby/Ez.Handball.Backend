using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableLineupConstraintsRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private ILineupConstraintsRepository Sut() => new TableLineupConstraintsRepository(new TableQuery(_client));

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.Config).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.Config).DeleteAsync();

    private async Task SeedAsync(params (string Key, string Value)[] rows)
    {
        var table = _client.GetTableClient(Tables.Config);
        foreach (var (key, value) in rows)
            await table.UpsertEntityAsync(new ConfigEntity
            {
                PartitionKey = "fantasy-lineup-v1", RowKey = key, Value = value
            }, TableUpdateMode.Replace);
    }

    [Fact]
    public async Task Get_WhenGroupAbsent_ReturnsNull()
    {
        Assert.Null(await Sut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_ParsesScalarsAndPositionMinMax()
    {
        await SeedAsync(
            ("starterCount", "7"),
            ("captainMultiplier", "2"),
            ("captainRequired", "true"),
            ("viceRequired", "false"),
            ("startMin:GK", "1"), ("startMax:GK", "1"),
            ("startMin:LB", "0"), ("startMax:LB", "3"));

        var c = await Sut().GetAsync(1, default);

        Assert.NotNull(c);
        Assert.Equal(7, c!.StarterCount);
        Assert.Equal(2, c.CaptainMultiplier);
        Assert.True(c.CaptainRequired);
        Assert.False(c.ViceRequired);
        Assert.Equal((1, 1), c.PositionStart["GK"]);
        Assert.Equal((0, 3), c.PositionStart["LB"]);
    }

    [Fact]
    public async Task Get_WhenRequiredKeyMissing_ReturnsNull()
    {
        await SeedAsync(("captainMultiplier", "2")); // no starterCount
        Assert.Null(await Sut().GetAsync(1, default));
    }
}
