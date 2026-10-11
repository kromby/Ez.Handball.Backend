using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableTransferCountRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private ITransferCountRepository Sut() => new TableTransferCountRepository(_client);
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameTransferCounts).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameTransferCounts).DeleteAsync();

    [Fact]
    public async Task List_EmptyForUnknownTeam()
        => Assert.Empty(await Sut().ListAsync(Team, default));

    [Fact]
    public async Task Increment_CreatesTheRoundRow()
    {
        await Sut().IncrementAsync(Team, "3", TransferType.Buy, T0, default);

        var row = Assert.Single(await Sut().ListAsync(Team, default));
        Assert.Equal(new RoundTransferCount("3", Buys: 1, Sells: 0), row);
    }

    [Fact]
    public async Task Increment_CountsBuysAndSellsSeparately()
    {
        await Sut().IncrementAsync(Team, "3", TransferType.Sell, T0, default);
        await Sut().IncrementAsync(Team, "3", TransferType.Buy, T0, default);
        await Sut().IncrementAsync(Team, "3", TransferType.Buy, T0, default);

        Assert.Equal(new RoundTransferCount("3", Buys: 2, Sells: 1), Assert.Single(await Sut().ListAsync(Team, default)));
    }

    [Fact]
    public async Task List_ReturnsOneRowPerRound_ForThatTeamOnly()
    {
        await Sut().IncrementAsync(Team, "3", TransferType.Buy, T0, default);
        await Sut().IncrementAsync(Team, "4", TransferType.Sell, T0, default);
        await Sut().IncrementAsync("u-2:fantasy", "3", TransferType.Buy, T0, default);

        var rows = await Sut().ListAsync(Team, default);

        Assert.Equal(2, rows.Count);
        Assert.Contains(new RoundTransferCount("3", 1, 0), rows);
        Assert.Contains(new RoundTransferCount("4", 0, 1), rows);
    }

    [Fact]
    public async Task ConcurrentIncrements_AreNotLost()
    {
        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Sut().IncrementAsync(Team, "3", TransferType.Buy, T0, default)));

        Assert.Equal(4, Assert.Single(await Sut().ListAsync(Team, default)).Buys);
    }
}
