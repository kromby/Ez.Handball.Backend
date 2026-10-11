using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableTransferConfigRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private TableTransferConfigRepository CreateSut() => new(_query.Object);

    private void SetupGroup(string group, params (string Key, string Value)[] rows) =>
        _query.Setup(q => q.QueryAsync<ConfigEntity>(
                Ez.Handball.Infrastructure.Tables.Config,
                $"PartitionKey eq '{ODataFilter.Escape(group)}'",
                default))
              .Returns(ToAsync(rows.Select(r => new ConfigEntity { PartitionKey = group, RowKey = r.Key, Value = r.Value })));

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetAsync_ReadsAllKeys()
    {
        SetupGroup("fantasy-transfers-v1",
            ("freeTransfersPerRound", "2"), ("maxBankedFreeTransfers", "3"),
            ("pointsPerExtraTransfer", "6"), ("startsFromRound", "7"));

        var cfg = await CreateSut().GetAsync(1, default);

        Assert.Equal(new TransferConfig(1, 2, 3, 6, 7), cfg);
    }

    [Fact]
    public async Task GetAsync_FallsBackToDefaults_ForMissingOrBadKeys()
    {
        SetupGroup("fantasy-transfers-v1", ("freeTransfersPerRound", "abc"));

        var cfg = await CreateSut().GetAsync(1, default);

        Assert.Equal(new TransferConfig(1, 1, 1, 4, 1), cfg);
    }

    [Fact]
    public async Task GetAsync_NullWhenGroupMissing()
    {
        SetupGroup("fantasy-transfers-v1");

        Assert.Null(await CreateSut().GetAsync(1, default));
    }
}
