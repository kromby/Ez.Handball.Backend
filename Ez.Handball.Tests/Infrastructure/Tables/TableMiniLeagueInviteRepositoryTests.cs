using Azure.Data.Tables;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

// Alias avoids the collision between this test namespace's trailing ".Tables" and the Tables class.
using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableMiniLeagueInviteRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private readonly ITableQuery _query;
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    public TableMiniLeagueInviteRepositoryTests() => _query = new TableQuery(_client);

    private TableMiniLeagueInviteRepository Sut() => new(_client, _query);

    public async Task InitializeAsync()
        => await _client.GetTableClient(Tables.MiniLeagueInvites).CreateIfNotExistsAsync();

    public async Task DisposeAsync()
        => await _client.GetTableClient(Tables.MiniLeagueInvites).DeleteAsync();

    [Fact]
    public async Task Add_ThenGetByToken_RoundTrips()
    {
        await Sut().AddAsync(new MiniLeagueInvite("tok-1", "lg-1", "u-1", T0, T0.AddDays(7)), default);

        var got = await Sut().GetByTokenAsync("tok-1", default);

        Assert.NotNull(got);
        Assert.Equal("tok-1", got!.Token);
        Assert.Equal("lg-1", got.LeagueId);
        Assert.Equal("u-1", got.CreatedByUserId);
        Assert.Equal(T0, got.CreatedAt);
        Assert.Equal(T0.AddDays(7), got.ExpiresAt);
    }

    [Fact]
    public async Task GetByToken_Unknown_ReturnsNull()
        => Assert.Null(await Sut().GetByTokenAsync("nope", default));

    [Fact]
    public async Task GetByLeague_ReturnsTheActiveInvite()
    {
        await Sut().AddAsync(new MiniLeagueInvite("tok-1", "lg-1", "u-1", T0, null), default);
        await Sut().AddAsync(new MiniLeagueInvite("tok-2", "lg-2", "u-2", T0, null), default);

        var got = await Sut().GetByLeagueAsync("lg-1", default);

        Assert.NotNull(got);
        Assert.Equal("tok-1", got!.Token);
    }

    [Fact]
    public async Task GetByLeague_None_ReturnsNull()
        => Assert.Null(await Sut().GetByLeagueAsync("lg-x", default));

    [Fact]
    public async Task DeleteByToken_RemovesRow_AndIsIdempotent()
    {
        await Sut().AddAsync(new MiniLeagueInvite("tok-1", "lg-1", "u-1", T0, null), default);

        await Sut().DeleteByTokenAsync("tok-1", default);
        Assert.Null(await Sut().GetByTokenAsync("tok-1", default));

        await Sut().DeleteByTokenAsync("tok-1", default); // missing row → no throw
    }

    [Fact]
    public async Task Add_WithNullExpiry_RoundTrips()
    {
        await Sut().AddAsync(new MiniLeagueInvite("tok-3", "lg-3", "u-3", T0, null), default);

        var got = await Sut().GetByTokenAsync("tok-3", default);

        Assert.NotNull(got);
        Assert.Null(got!.ExpiresAt);
    }
}
