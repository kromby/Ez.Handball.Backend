using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableUserRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);
    private IUserRepository _sut = null!;

    public async Task InitializeAsync()
    {
        _sut = new TableUserRepository(_client);
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.Users).CreateIfNotExistsAsync();
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.UserEmailIndex).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
    {
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.Users).DeleteAsync();
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.UserEmailIndex).DeleteAsync();
    }

    private static UserEntity User(string id, string email) => new()
    {
        RowKey = id, Email = email, DisplayName = "Jón", Language = "is",
        FavoriteClubId = "385", CreatedAt = DateTimeOffset.UnixEpoch, ChangedAt = DateTimeOffset.UnixEpoch
    };

    [Fact]
    public async Task AddAndGetById_RoundTrips()
    {
        await _sut.AddAsync(User("u-1", "a@b.is"), default);
        var loaded = await _sut.GetByIdAsync("u-1", default);
        Assert.NotNull(loaded);
        Assert.Equal("a@b.is", loaded!.Email);
        Assert.Equal("385", loaded.FavoriteClubId);
    }

    [Fact]
    public async Task GetById_Missing_ReturnsNull()
        => Assert.Null(await _sut.GetByIdAsync("nope", default));

    [Fact]
    public async Task ReserveEmail_ThenGetByEmail_ResolvesToUser()
    {
        await _sut.AddAsync(User("u-2", "c@d.is"), default);
        Assert.True(await _sut.TryReserveEmailAsync("c@d.is", "u-2", default));
        var loaded = await _sut.GetByEmailAsync("c@d.is", default);
        Assert.NotNull(loaded);
        Assert.Equal("u-2", loaded!.RowKey);
    }

    [Fact]
    public async Task GetByEmail_NoIndexRow_ReturnsNull()
        => Assert.Null(await _sut.GetByEmailAsync("missing@x.is", default));

    [Fact]
    public async Task ReserveEmail_SecondTimeForSameEmail_ReturnsFalse()
    {
        Assert.True(await _sut.TryReserveEmailAsync("dup@x.is", "u-3", default));
        Assert.False(await _sut.TryReserveEmailAsync("dup@x.is", "u-4", default));
    }

    [Fact]
    public async Task Update_ReplacesFields()
    {
        await _sut.AddAsync(User("u-5", "e@f.is"), default);
        var user = await _sut.GetByIdAsync("u-5", default);
        user!.EmailVerified = true;
        user.DisplayName = "Páll";
        await _sut.UpdateAsync(user, default);
        var reloaded = await _sut.GetByIdAsync("u-5", default);
        Assert.True(reloaded!.EmailVerified);
        Assert.Equal("Páll", reloaded.DisplayName);
    }
}
