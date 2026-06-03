using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

[Collection("Azurite")]
public class TableShortlistRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);
    private IShortlistRepository _sut = null!;

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset T1 = DateTimeOffset.UnixEpoch.AddDays(1);

    public async Task InitializeAsync()
    {
        _sut = new TableShortlistRepository(_client, new TableQuery(_client));
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.Shortlists).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
        => await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.Shortlists).DeleteAsync();

    [Fact]
    public async Task Upsert_ThenGet_RoundTripsActiveEntry()
    {
        await _sut.UpsertAsync("u-1", "p-1", T0, deletedAt: null, default);
        var entry = await _sut.GetAsync("u-1", "p-1", default);
        Assert.NotNull(entry);
        Assert.Equal("p-1", entry!.PlayerId);
        Assert.Equal(T0, entry.CreatedAt);
        Assert.Null(entry.DeletedAt);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
        => Assert.Null(await _sut.GetAsync("u-1", "nope", default));

    [Fact]
    public async Task SoftDelete_SetsDeletedAt_AndExcludesFromActive()
    {
        await _sut.UpsertAsync("u-1", "p-1", T0, deletedAt: null, default);
        await _sut.UpsertAsync("u-1", "p-1", T0, deletedAt: T1, default);

        var entry = await _sut.GetAsync("u-1", "p-1", default);
        Assert.Equal(T1, entry!.DeletedAt);
        Assert.Equal(0, await _sut.CountActiveAsync("u-1", default));
        Assert.Empty(await _sut.ListActiveAsync("u-1", default));
    }

    [Fact]
    public async Task CountActive_And_ListActive_ExcludeDeleted_AndScopeToUser()
    {
        await _sut.UpsertAsync("u-1", "p-1", T0, deletedAt: null, default);
        await _sut.UpsertAsync("u-1", "p-2", T0, deletedAt: null, default);
        await _sut.UpsertAsync("u-1", "p-3", T0, deletedAt: T1, default);   // deleted
        await _sut.UpsertAsync("u-2", "p-9", T0, deletedAt: null, default); // other user

        Assert.Equal(2, await _sut.CountActiveAsync("u-1", default));
        var list = await _sut.ListActiveAsync("u-1", default);
        Assert.Equal(2, list.Count);
        Assert.DoesNotContain(list, e => e.PlayerId == "p-3");
        Assert.DoesNotContain(list, e => e.PlayerId == "p-9");
    }

    [Fact]
    public async Task Reactivate_ClearsDeletedAt_AndResetsCreatedAt()
    {
        await _sut.UpsertAsync("u-1", "p-1", T0, deletedAt: T1, default); // soft-deleted
        await _sut.UpsertAsync("u-1", "p-1", T1, deletedAt: null, default); // re-added
        var entry = await _sut.GetAsync("u-1", "p-1", default);
        Assert.Null(entry!.DeletedAt);
        Assert.Equal(T1, entry.CreatedAt);
        Assert.Equal(1, await _sut.CountActiveAsync("u-1", default));
    }
}
