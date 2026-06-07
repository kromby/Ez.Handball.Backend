using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameRosterRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IGameRosterRepository Sut() => new TableGameRosterRepository(_client, new TableQuery(_client));
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset T1 = DateTimeOffset.UnixEpoch.AddDays(1);
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameRosters).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameRosters).DeleteAsync();

    [Fact]
    public async Task Add_ThenListActive_ReturnsSlot()
    {
        Assert.Equal(RosterAddOutcome.Added, await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default));
        var active = await Sut().ListActiveAsync(Team, default);
        var entry = Assert.Single(active);
        Assert.Equal("p-1", entry.PlayerId);
        Assert.Equal("VS", entry.Position);
        Assert.Equal(40_000_000, entry.PricePaidAmount);
    }

    [Fact]
    public async Task Add_WhenActiveExists_ReturnsAlreadyActive()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        Assert.Equal(RosterAddOutcome.AlreadyActive,
            await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 99_000_000, T1, default));
        // Price unchanged by the rejected re-add.
        Assert.Equal(40_000_000, (await Sut().GetAsync(Team, "p-1", default))!.PricePaidAmount);
    }

    [Fact]
    public async Task SoftDelete_RemovesFromActive_ThenResurrectRelocksPrice()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        Assert.Empty(await Sut().ListActiveAsync(Team, default));
        Assert.NotNull((await Sut().GetAsync(Team, "p-1", default))!.DeletedAt);

        // Re-buying a sold player resurrects the row with a fresh price and clears DeletedAt.
        Assert.Equal(RosterAddOutcome.Added,
            await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 55_000_000, T1, default));
        var entry = Assert.Single(await Sut().ListActiveAsync(Team, default));
        Assert.Equal(55_000_000, entry.PricePaidAmount);
    }

    [Fact]
    public async Task SoftDelete_IsIdempotent()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        // First soft-delete succeeds.
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        // Second soft-delete on an already-deleted row is a silent no-op.
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        // Row remains soft-deleted and absent from the active set.
        Assert.Empty(await Sut().ListActiveAsync(Team, default));
        Assert.NotNull((await Sut().GetAsync(Team, "p-1", default))!.DeletedAt);
    }

    [Fact]
    public async Task ListActive_IsScopedByTeam()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        await Sut().AddOrResurrectAsync("u-2:fantasy", "p-9", "VS", 10_000_000, T0, default);
        Assert.Single(await Sut().ListActiveAsync(Team, default));
    }
}
