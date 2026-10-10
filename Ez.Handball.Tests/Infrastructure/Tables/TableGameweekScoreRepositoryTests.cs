using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameweekScoreRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IGameweekScoreRepository Sut() => new TableGameweekScoreRepository(_client, new TableQuery(_client));
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameweekScores).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameweekScores).DeleteAsync();

    private static GameweekScore Score(string round) =>
        new(Team, round, 10, null, Array.Empty<GameweekPlayerScore>());

    [Fact]
    public async Task Delete_RemovesOnlyThatRound()
    {
        await Sut().SaveAsync(Score("1"), default);
        await Sut().SaveAsync(Score("2"), default);

        await Sut().DeleteAsync(Team, "1", default);

        var left = await Sut().ListByTeamAsync(Team, default);
        Assert.Equal(new[] { "2" }, left.Select(s => s.RoundLabel));
    }

    [Fact]
    public async Task Delete_WhenMissing_IsNoOp()
    {
        await Sut().DeleteAsync(Team, "1", default);

        Assert.Empty(await Sut().ListByTeamAsync(Team, default));
    }
}
