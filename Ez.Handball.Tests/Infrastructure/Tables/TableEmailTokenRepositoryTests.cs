using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

[Collection("Azurite")]
public class TableEmailTokenRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IEmailTokenRepository _sut = null!;

    public async Task InitializeAsync()
    {
        _sut = new TableEmailTokenRepository(_client);
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.EmailTokens).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
        => await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.EmailTokens).DeleteAsync();

    private static EmailTokenEntity Token(string purpose, string hash, string userId) => new()
    {
        PartitionKey = purpose, RowKey = hash, UserId = userId,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddHours(24)
    };

    [Theory]
    [InlineData("verify")]
    [InlineData("reset")]
    public async Task Add_Get_Delete_RoundTrips(string purpose)
    {
        await _sut.AddAsync(Token(purpose, "hashA", "u-1"), default);

        var loaded = await _sut.GetAsync(purpose, "hashA", default);
        Assert.NotNull(loaded);
        Assert.Equal("u-1", loaded!.UserId);

        await _sut.DeleteAsync(purpose, "hashA", default);
        Assert.Null(await _sut.GetAsync(purpose, "hashA", default));
    }

    [Fact]
    public async Task Get_WrongPurpose_ReturnsNull()
    {
        await _sut.AddAsync(Token("verify", "hashB", "u-2"), default);
        Assert.Null(await _sut.GetAsync("reset", "hashB", default)); // same hash, different partition
    }
}
