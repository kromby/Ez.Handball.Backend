using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableSquadRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);
    private readonly Mock<ISquadConstraintsRepository> _constraints = new();
    private ISquadRepository _sut = null!;

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset T1 = DateTimeOffset.UnixEpoch.AddDays(1);

    public async Task InitializeAsync()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int>(), 100_000_000, "ISK"));
        _sut = new TableSquadRepository(_client, new TableQuery(_client), _constraints.Object);
        await _client.GetTableClient(Tables.Squads).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
        => await _client.GetTableClient(Tables.Squads).DeleteAsync();

    private async Task InsertAsync(string userId, string playerId, string? position,
        double pricePaid, DateTimeOffset? deletedAt)
        => await _client.GetTableClient(Tables.Squads).UpsertEntityAsync(new SquadEntryEntity
        {
            PartitionKey = userId,
            RowKey = playerId,
            Position = position,
            PricePaidAmount = pricePaid,
            PricePaidCurrency = "ISK",
            CreatedAt = T0,
            DeletedAt = deletedAt
        });

    [Fact]
    public async Task Get_ReturnsActiveSlots_WithLockedPricePaid()
    {
        await InsertAsync("u-1", "p-1", "VS", 40_000_000, deletedAt: null);
        await InsertAsync("u-1", "p-2", "MM", 30_000_000, deletedAt: null);

        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Equal(2, squad.Players.Count);
        var p1 = Assert.Single(squad.Players, p => p.PlayerId == "p-1");
        Assert.Equal("VS", p1.Position);
        Assert.Equal(40_000_000, p1.PricePaid.Amount);
        Assert.Equal("ISK", p1.PricePaid.Currency);
        Assert.Equal("ISK", squad.Currency);
    }

    [Fact]
    public async Task Get_DerivesBudget_AsStartingCapMinusPricePaid()
    {
        await InsertAsync("u-1", "p-1", "VS", 40_000_000, deletedAt: null);
        await InsertAsync("u-1", "p-2", "MM", 30_000_000, deletedAt: null);

        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Equal(30_000_000, squad.Budget); // 100M cap - 70M paid
    }

    [Fact]
    public async Task Get_ExcludesSoftDeleted_AndScopesToUser()
    {
        await InsertAsync("u-1", "p-1", "VS", 40_000_000, deletedAt: null);
        await InsertAsync("u-1", "p-2", "MM", 30_000_000, deletedAt: T1);    // sold
        await InsertAsync("u-2", "p-9", "VS", 10_000_000, deletedAt: null);  // other user

        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Single(squad.Players);
        Assert.Equal("p-1", squad.Players[0].PlayerId);
        Assert.Equal(60_000_000, squad.Budget); // 100M - 40M; soft-deleted not counted
    }

    [Fact]
    public async Task Get_EmptySquad_ReturnsStartingCapBudget()
    {
        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Empty(squad.Players);
        Assert.Equal(100_000_000, squad.Budget);
        Assert.Equal("ISK", squad.Currency);
    }

    [Fact]
    public async Task Get_MissingConstraints_FallsBackToZeroBudgetAndIsk()
    {
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((SquadConstraints?)null);

        var squad = await _sut.GetAsync("u-1", GameFlavor.Fantasy, default);

        Assert.Empty(squad.Players);
        Assert.Equal(0, squad.Budget);
        Assert.Equal("ISK", squad.Currency);
    }
}
