using Azure.Data.Tables;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameweekScoreRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private TableGameweekScoreRepository Sut() => new(_client, new TableQuery(_client));
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameweekScores).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameweekScores).DeleteAsync();

    [Fact]
    public async Task Save_ThenList_RoundTripsTheTransferHit()
    {
        await Sut().SaveAsync(new GameweekScore(Team, "7", 5, null, [], TransferHit: 4), default);

        var score = Assert.Single(await Sut().ListByTeamAsync(Team, default));
        Assert.Equal(4, score.TransferHit);
        Assert.Equal(5, score.Points);
    }

    [Fact]
    public async Task RowsWrittenBeforeHitsExisted_ReadAsNoHit()
    {
        // A score row from before #171 has no TransferHit column.
        await _client.GetTableClient(Tables.GameweekScores).UpsertEntityAsync(new TableEntity(Team, "3")
        {
            ["Points"] = 12.0, ["BreakdownJson"] = "[]"
        });

        var score = Assert.Single(await Sut().ListByTeamAsync(Team, default));
        Assert.Equal(0, score.TransferHit);
        Assert.Equal(12, score.Points);
    }
}
