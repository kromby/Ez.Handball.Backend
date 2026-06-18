using Azure;
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Xunit;

namespace Ez.Handball.Tests.Infrastructure;

public class TableClockOverrideStoreTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private const string TableName = "TestClockOverrideStore";
    private TableServiceClient _serviceClient = null!;
    private TableClient _table = null!;

    public async Task InitializeAsync()
    {
        _serviceClient = new TableServiceClient(ConnectionString);
        _table = _serviceClient.GetTableClient(TableName);
        await _table.CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _table.DeleteAsync();

    private TableClockOverrideStore Store() => new(_serviceClient, TableName);
    private GameClock Clock() => new(overrideEnabled: true, _serviceClient, TableName);

    [Fact]
    public async Task SetAsync_WritesInstant_GameClockReadsItBack()
    {
        var instant = new DateTimeOffset(2025, 9, 1, 17, 0, 0, TimeSpan.Zero);

        await Store().SetAsync(instant, default);

        Assert.Equal(instant, Clock().GetUtcNow());
    }

    [Fact]
    public async Task ClearAsync_RemovesRow_GameClockFallsBackToWallClock()
    {
        await Store().SetAsync(new DateTimeOffset(2025, 9, 1, 17, 0, 0, TimeSpan.Zero), default);

        await Store().ClearAsync(default);

        Assert.True((DateTimeOffset.UtcNow - Clock().GetUtcNow()).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ClearAsync_WhenAbsent_DoesNotThrow()
    {
        await Store().ClearAsync(default); // row never written
    }
}
