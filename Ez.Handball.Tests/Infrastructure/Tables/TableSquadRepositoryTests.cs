using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableSquadRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private readonly Mock<ISquadConstraintsRepository> _constraints = new();
    private IGameRosterRepository _roster = null!;
    private IGameBudgetRepository _budget = null!;
    private ISquadRepository _sut = null!;
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int>(), 100_000_000, "ISK"));
        _roster = new TableGameRosterRepository(_client, new TableQuery(_client));
        _budget = new TableGameBudgetRepository(_client);
        _sut = new TableSquadRepository(_roster, _budget, _constraints.Object);
        await _client.GetTableClient(Tables.GameRosters).CreateIfNotExistsAsync();
        await _client.GetTableClient(Tables.GameBudgets).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
    {
        await _client.GetTableClient(Tables.GameRosters).DeleteAsync();
        await _client.GetTableClient(Tables.GameBudgets).DeleteAsync();
    }

    [Fact]
    public async Task Get_ReturnsActiveSlots_AndStoredBalance()
    {
        await _roster.AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        await _roster.AddOrResurrectAsync(Team, "p-2", "MM", 30_000_000, T0, default);
        await _budget.CreateAsync(Team, 30_000_000, T0, default); // stored balance, NOT derived

        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Equal(2, squad.Players.Count);
        var p1 = Assert.Single(squad.Players, p => p.PlayerId == "p-1");
        Assert.Equal("VS", p1.Position);
        Assert.Equal(40_000_000, p1.PricePaid.Amount);
        Assert.Equal("ISK", p1.PricePaid.Currency);
        Assert.Equal(30_000_000, squad.Budget);  // read from the budget row, not recomputed
        Assert.Equal("ISK", squad.Currency);
    }

    [Fact]
    public async Task Get_NoBudgetRow_BudgetIsZero()
    {
        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);
        Assert.Empty(squad.Players);
        Assert.Equal(0, squad.Budget);
        Assert.Equal("ISK", squad.Currency);
    }

    [Fact]
    public async Task Get_MissingConstraints_FallsBackToIsk_AndZeroBudgetWhenNoRow()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SquadConstraints?)null);

        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Empty(squad.Players);
        Assert.Equal(0, squad.Budget);     // no budget row -> 0
        Assert.Equal("ISK", squad.Currency); // fallback currency
    }

    [Fact]
    public async Task Get_ExcludesSoftDeleted()
    {
        await _roster.AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        await _roster.AddOrResurrectAsync(Team, "p-2", "MM", 30_000_000, T0, default);
        await _roster.SoftDeleteAsync(Team, "p-2", T0, default);
        await _budget.CreateAsync(Team, 60_000_000, T0, default);

        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Single(squad.Players);
        Assert.Equal("p-1", squad.Players[0].PlayerId);
    }
}
