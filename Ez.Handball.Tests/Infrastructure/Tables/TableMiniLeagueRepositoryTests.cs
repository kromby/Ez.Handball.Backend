using Azure.Data.Tables;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableMiniLeagueRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private readonly ITableQuery _query;
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    public TableMiniLeagueRepositoryTests() => _query = new TableQuery(_client);

    private TableMiniLeagueRepository Sut() => new(_client, _query);

    public async Task InitializeAsync()
    {
        await _client.GetTableClient(Tables.MiniLeagues).CreateIfNotExistsAsync();
        await _client.GetTableClient(Tables.MiniLeagueMembers).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
    {
        await _client.GetTableClient(Tables.MiniLeagues).DeleteAsync();
        await _client.GetTableClient(Tables.MiniLeagueMembers).DeleteAsync();
    }

    [Fact]
    public async Task Create_ThenGet_RoundTripsHeader()
    {
        await Sut().CreateAsync(new MiniLeague("lg-1", "Office League", "2025-26", "u-1", T0), default);

        var got = await Sut().GetAsync("lg-1", default);

        Assert.NotNull(got);
        Assert.Equal("lg-1", got!.Id);
        Assert.Equal("Office League", got.Name);
        Assert.Equal("2025-26", got.Season);
        Assert.Equal("u-1", got.CreatorUserId);
        Assert.Equal(T0, got.CreatedAt);
    }

    [Fact]
    public async Task Get_MissingLeague_ReturnsNull()
        => Assert.Null(await Sut().GetAsync("does-not-exist", default));

    [Fact]
    public async Task AddMember_ThenGetMembers_ReturnsAllRows()
    {
        await Sut().AddMemberAsync("lg-1", new MiniLeagueMember("u-1", "creator", T0), default);
        await Sut().AddMemberAsync("lg-1", new MiniLeagueMember("u-2", "member", T0), default);

        var members = await Sut().GetMembersAsync("lg-1", default);

        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.UserId == "u-1" && m.Role == "creator");
        Assert.Contains(members, m => m.UserId == "u-2" && m.Role == "member");
    }

    [Fact]
    public async Task GetMembers_IsScopedByLeague()
    {
        await Sut().AddMemberAsync("lg-1", new MiniLeagueMember("u-1", "creator", T0), default);
        await Sut().AddMemberAsync("lg-2", new MiniLeagueMember("u-2", "creator", T0), default);

        var members = await Sut().GetMembersAsync("lg-1", default);

        Assert.Single(members);
        Assert.Equal("u-1", members[0].UserId);
    }
}
