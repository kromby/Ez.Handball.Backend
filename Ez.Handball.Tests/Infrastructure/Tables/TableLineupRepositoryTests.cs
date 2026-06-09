using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableLineupRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private ILineupRepository Sut() => new TableLineupRepository(_client, new TableQuery(_client));
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameLineups).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameLineups).DeleteAsync();

    private static Lineup Sample() => new(new[]
    {
        new LineupSlot("p0", LineupRole.Captain, null),
        new LineupSlot("p1", LineupRole.Vice, null),
        new LineupSlot("p2", LineupRole.Starter, null),
        new LineupSlot("p3", LineupRole.Bench, 0),
        new LineupSlot("p4", LineupRole.Bench, 1),
    });

    [Fact]
    public async Task Get_WhenNeverSet_ReturnsNull()
    {
        Assert.Null(await Sut().GetAsync(Team, default));
    }

    [Fact]
    public async Task Replace_ThenGet_RoundTripsRolesAndBenchOrder()
    {
        await Sut().ReplaceAsync(Team, Sample(), default);
        var got = await Sut().GetAsync(Team, default);

        Assert.NotNull(got);
        Assert.Equal(5, got!.Slots.Count);
        Assert.Equal(LineupRole.Captain, got.Slots.Single(s => s.PlayerId == "p0").Role);
        Assert.Equal(LineupRole.Vice, got.Slots.Single(s => s.PlayerId == "p1").Role);
        var bench = got.Slots.Where(s => s.Role == LineupRole.Bench).OrderBy(s => s.BenchOrder).ToList();
        Assert.Equal(new[] { "p3", "p4" }, bench.Select(s => s.PlayerId));
        Assert.Equal(new int?[] { 0, 1 }, bench.Select(s => s.BenchOrder));
    }

    [Fact]
    public async Task Replace_DropsRowsNoLongerPresent()
    {
        await Sut().ReplaceAsync(Team, Sample(), default);
        // New lineup with only p0 and p9 — p1..p4 must be removed.
        var next = new Lineup(new[]
        {
            new LineupSlot("p0", LineupRole.Starter, null),
            new LineupSlot("p9", LineupRole.Bench, 0),
        });
        await Sut().ReplaceAsync(Team, next, default);

        var got = await Sut().GetAsync(Team, default);
        Assert.Equal(new[] { "p0", "p9" }, got!.Slots.Select(s => s.PlayerId).OrderBy(x => x));
    }

    [Fact]
    public async Task Replace_IsScopedByTeam()
    {
        await Sut().ReplaceAsync(Team, Sample(), default);
        await Sut().ReplaceAsync("u-2:fantasy", new Lineup(new[]
        {
            new LineupSlot("x0", LineupRole.Starter, null),
        }), default);

        var got = await Sut().GetAsync(Team, default);
        Assert.Equal(5, got!.Slots.Count);
    }
}
