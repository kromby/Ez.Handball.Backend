using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameBudgetRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IGameBudgetRepository Sut() => new TableGameBudgetRepository(_client);
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameBudgets).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameBudgets).DeleteAsync();

    [Fact]
    public async Task Get_NullBeforeCreate_ThenReturnsAmount()
    {
        Assert.Null(await Sut().GetBalanceAsync(Team, default));
        await Sut().CreateAsync(Team, 100_000_000, T0, default);
        Assert.Equal(100_000_000, await Sut().GetBalanceAsync(Team, default));
    }

    [Fact]
    public async Task Deduct_ReducesBalance()
    {
        await Sut().CreateAsync(Team, 100_000_000, T0, default);
        Assert.True(await Sut().TryDeductAsync(Team, 40_000_000, T0, default));
        Assert.Equal(60_000_000, await Sut().GetBalanceAsync(Team, default));
    }

    [Fact]
    public async Task Deduct_RefusesToGoNegative()
    {
        await Sut().CreateAsync(Team, 30_000_000, T0, default);
        Assert.False(await Sut().TryDeductAsync(Team, 40_000_000, T0, default));
        Assert.Equal(30_000_000, await Sut().GetBalanceAsync(Team, default)); // unchanged
    }

    [Fact]
    public async Task Credit_IncreasesBalance()
    {
        await Sut().CreateAsync(Team, 60_000_000, T0, default);
        Assert.True(await Sut().TryCreditAsync(Team, 45_000_000, T0, default));
        Assert.Equal(105_000_000, await Sut().GetBalanceAsync(Team, default));
    }

    [Fact]
    public async Task Deduct_OnMissingRow_ReturnsFalse()
        => Assert.False(await Sut().TryDeductAsync("nope:fantasy", 1, T0, default));

    [Fact]
    public async Task Credit_OnMissingRow_ReturnsFalse()
        => Assert.False(await Sut().TryCreditAsync("nope:fantasy", 1, T0, default));

    [Fact]
    public async Task ConcurrentCredits_AllApplied_NoLostUpdates()
    {
        await Sut().CreateAsync(Team, 0, T0, default);
        // N=5 keeps worst-case retries (N-1=4) within MaxRetries=5
        const int n = 5;
        var results = await Task.WhenAll(Enumerable.Range(0, n)
            .Select(_ => Sut().TryCreditAsync(Team, 1_000_000, T0, default)));
        Assert.All(results, r => Assert.True(r));
        Assert.Equal(n * 1_000_000, await Sut().GetBalanceAsync(Team, default));
    }
}
