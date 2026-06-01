using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableRefreshTokenRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IRefreshTokenRepository _sut = null!;

    public async Task InitializeAsync()
    {
        _sut = new TableRefreshTokenRepository(_client);
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.RefreshTokens).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
        => await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.RefreshTokens).DeleteAsync();

    private static RefreshTokenEntity Token(string userId, string hash) => new()
    {
        PartitionKey = userId, RowKey = hash,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddDays(30), CreatedAt = DateTimeOffset.UnixEpoch
    };

    [Fact]
    public async Task Add_ThenGet_RoundTrips()
    {
        await _sut.AddAsync(Token("u-1", "hashA"), default);
        var loaded = await _sut.GetAsync("u-1", "hashA", default);
        Assert.NotNull(loaded);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(30), loaded!.ExpiresAt);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
        => Assert.Null(await _sut.GetAsync("u-1", "nope", default));

    [Fact]
    public async Task Delete_RemovesOneRow()
    {
        await _sut.AddAsync(Token("u-2", "hashB"), default);
        await _sut.DeleteAsync("u-2", "hashB", default);
        Assert.Null(await _sut.GetAsync("u-2", "hashB", default));
    }

    [Fact]
    public async Task Delete_Missing_DoesNotThrow()
        => await _sut.DeleteAsync("u-2", "ghost", default);

    [Fact]
    public async Task DeleteAllForUser_ClearsThatPartitionOnly()
    {
        await _sut.AddAsync(Token("u-3", "h1"), default);
        await _sut.AddAsync(Token("u-3", "h2"), default);
        await _sut.AddAsync(Token("u-4", "h3"), default);
        await _sut.DeleteAllForUserAsync("u-3", default);
        Assert.Null(await _sut.GetAsync("u-3", "h1", default));
        Assert.Null(await _sut.GetAsync("u-3", "h2", default));
        Assert.NotNull(await _sut.GetAsync("u-4", "h3", default));
    }
}
