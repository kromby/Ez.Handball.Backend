using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableGameweekConfigRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private TableGameweekConfigRepository CreateSut() => new(_query.Object);

    private void SetupGroup(string group, params (string Key, string Value)[] rows) =>
        _query.Setup(q => q.QueryAsync<ConfigEntity>(
                Ez.Handball.Infrastructure.Tables.Config,
                $"PartitionKey eq '{ODataFilter.Escape(group)}'",
                default))
              .Returns(ToAsync(rows.Select(r => new ConfigEntity
              {
                  PartitionKey = group,
                  RowKey = r.Key,
                  Value = r.Value
              })));

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetAsync_ReadsMatchFinalBufferHours_WhenPresent()
    {
        SetupGroup("fantasy-gameweek-v1",
            ("tournamentId", "8444"), ("lockOffsetHours", "1"),
            ("scoringRuleSetVersion", "1"), ("lineupConstraintsVersion", "1"),
            ("matchFinalBufferHours", "5"));

        var cfg = await CreateSut().GetAsync(1, default);

        Assert.NotNull(cfg);
        Assert.Equal(5d, cfg!.MatchFinalBufferHours);
    }

    [Fact]
    public async Task GetAsync_FallsBackTo3_WhenBufferKeyAbsent()
    {
        SetupGroup("fantasy-gameweek-v1",
            ("tournamentId", "8444"), ("lockOffsetHours", "1"),
            ("scoringRuleSetVersion", "1"), ("lineupConstraintsVersion", "1"));

        var cfg = await CreateSut().GetAsync(1, default);

        Assert.NotNull(cfg);
        Assert.Equal(3d, cfg!.MatchFinalBufferHours);
    }
}
