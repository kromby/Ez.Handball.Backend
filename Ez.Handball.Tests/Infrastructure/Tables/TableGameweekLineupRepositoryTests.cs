using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameweekLineupRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IGameweekLineupRepository Sut() => new TableGameweekLineupRepository(_client, new TableQuery(_client));
    private const string Team = "u-1:fantasy";
    private const string Round = "1";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameweekLineups).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameweekLineups).DeleteAsync();

    [Fact]
    public async Task SaveSnapshot_ThenGet_PreservesPlayAsAndNullStaysNull()
    {
        var lineup = new Lineup(new[]
        {
            new LineupSlot("p0", LineupRole.Starter, null, "LB"),
            new LineupSlot("p1", LineupRole.Bench, 0),
        });

        await Sut().SaveSnapshotAsync(Team, Round, lineup, default);
        var got = await Sut().GetSnapshotAsync(Team, Round, default);

        Assert.NotNull(got);
        Assert.Equal("LB", got!.Slots.Single(s => s.PlayerId == "p0").PlayAs);
        Assert.Null(got.Slots.Single(s => s.PlayerId == "p1").PlayAs);
    }

    [Fact]
    public async Task GetSnapshot_LegacyRowWithoutPlayAs_ReturnsNullPlayAs()
    {
        // Rows frozen before #166 have no PlayAs column at all.
        var table = _client.GetTableClient(Tables.GameweekLineups);
        await table.CreateIfNotExistsAsync();
        await table.UpsertEntityAsync(new GameweekLineupEntity
        {
            PartitionKey = $"{Team}|{Round}",
            RowKey = "p0",
            Role = LineupRole.Captain.ToString(),
            BenchOrder = null,
        }, TableUpdateMode.Replace);

        var got = await Sut().GetSnapshotAsync(Team, Round, default);

        Assert.NotNull(got);
        var slot = Assert.Single(got!.Slots);
        Assert.Equal(LineupRole.Captain, slot.Role);
        Assert.Null(slot.PlayAs);
    }
}
