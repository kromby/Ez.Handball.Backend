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
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", T0, default);
        Assert.True(await Sut().ExistsAsync("u-1", GameFlavor.Fantasy, default));
    }

    [Fact]
    public async Task Create_WritesTeamIdAndName_KeyedByUserAndFlavor()
    {
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", T0, default);
        var row = (await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.GameTeams)
            .GetEntityAsync<GameTeamEntity>("u-1", "fantasy")).Value;
        Assert.Equal("u-1:fantasy", row.TeamId);
        Assert.Equal("Dream Team", row.Name);
    }

    [Fact]
    public async Task Exists_IsScopedByFlavor()
    {
        await Sut().CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", T0, default);
        Assert.False(await Sut().ExistsAsync("u-1", GameFlavor.Manager, default));
    }
}
