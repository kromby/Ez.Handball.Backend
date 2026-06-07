using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameTeamRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IGameTeamRepository Sut() => new TableGameTeamRepository(_client);
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    public async Task InitializeAsync() => await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.GameTeams).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.GameTeams).DeleteAsync();

    [Fact]
    public async Task Exists_FalseBeforeCreate_TrueAfter()
    {
        Assert.False(await Sut().ExistsAsync("u-1", GameFlavor.Fantasy, default));
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#1E88E5", T0, default);
        Assert.True(await Sut().ExistsAsync("u-1", GameFlavor.Fantasy, default));
    }

    [Fact]
    public async Task Create_WritesTeamIdNameAndColor_KeyedByUserAndFlavor()
    {
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#43A047", T0, default);
        var row = (await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.GameTeams)
            .GetEntityAsync<GameTeamEntity>("u-1", "fantasy")).Value;
        Assert.Equal("u-1:fantasy", row.TeamId);
        Assert.Equal("Dream Team", row.Name);
        Assert.Equal("#43A047", row.Color);
    }

    [Fact]
    public async Task Exists_IsScopedByFlavor()
    {
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#1E88E5", T0, default);
        Assert.False(await Sut().ExistsAsync("u-1", GameFlavor.Manager, default));
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenMissing()
    {
        Assert.Null(await Sut().GetAsync("nobody", GameFlavor.Fantasy, default));
    }

    [Fact]
    public async Task Get_ReturnsTeam_AfterCreate()
    {
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#E53935", T0, default);
        var team = await Sut().GetAsync("u-1", GameFlavor.Fantasy, default);
        Assert.NotNull(team);
        Assert.Equal("u-1:fantasy", team!.TeamId);
        Assert.Equal("Dream Team", team.Name);
        Assert.Equal("#E53935", team.Color);
    }

    [Fact]
    public async Task Rename_UpdatesName_PreservesColor()
    {
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Old Name", "#FB8C00", T0, default);
        await Sut().RenameAsync("u-1", GameFlavor.Fantasy, "New Name", default);
        var team = await Sut().GetAsync("u-1", GameFlavor.Fantasy, default);
        Assert.Equal("New Name", team!.Name);
        Assert.Equal("#FB8C00", team.Color);
        Assert.Equal(T0, team.CreatedAt);
    }
}
