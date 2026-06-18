using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

public class TableLineupRepositoryUnitTests
{
    [Fact]
    public async Task ListTeamIdsAsync_ReturnsDistinctPartitionKeys()
    {
        var rows = new[]
        {
            new GameLineupEntity { PartitionKey = "u1:fantasy", RowKey = "p1" },
            new GameLineupEntity { PartitionKey = "u1:fantasy", RowKey = "p2" },
            new GameLineupEntity { PartitionKey = "u2:fantasy", RowKey = "p1" },
        };
        var query = new Mock<ITableQuery>();
        query.Setup(q => q.QueryAsync<GameLineupEntity>(Tables.GameLineups, null, It.IsAny<CancellationToken>()))
            .Returns(ToAsync(rows));

        var repo = new TableLineupRepository(new TableServiceClient("UseDevelopmentStorage=true"), query.Object);

        var ids = await repo.ListTeamIdsAsync(default);

        Assert.Equal(new[] { "u1:fantasy", "u2:fantasy" }, ids.OrderBy(x => x));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }
}
