using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableScoringRuleSetRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private TableScoringRuleSetRepository CreateSut() => new(_query.Object);

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

    private static (string, string)[] FullFantasyV1 =>
        new[] { ("goals", "2"), ("yellowCards", "-1"), ("twoMinute", "-2"), ("redCards", "-5"), ("appearances", "1") };

    private static (string, string)[] FullFantasyV2 =>
        new[]
        {
            ("goals", "2"), ("yellowCards", "-1"), ("twoMinute", "-2"), ("redCards", "-5"), ("appearances", "1"),
            ("assists", "1"), ("steals", "1"), ("blocks", "1"), ("saves", "0.5")
        };

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetAsync_AssemblesTypedRuleSet_FromGroupRows()
    {
        SetupGroup("fantasy-v1", FullFantasyV1);

        var rs = await CreateSut().GetAsync(GameFlavor.Fantasy, 1, default);

        Assert.NotNull(rs);
        Assert.Equal(GameFlavor.Fantasy, rs!.Flavor);
        Assert.Equal(1, rs.Version);
        Assert.Equal(2, rs.GoalPoints);
        Assert.Equal(-1, rs.YellowCardPoints);
        Assert.Equal(-2, rs.TwoMinutePoints);
        Assert.Equal(-5, rs.RedCardPoints);
        Assert.Equal(1, rs.AppearancePoints);
        Assert.Equal(0, rs.AssistPoints);
        Assert.Equal(0, rs.StealPoints);
        Assert.Equal(0, rs.BlockPoints);
        Assert.Equal(0, rs.SavePoints);

        _query.Verify(q => q.QueryAsync<ConfigEntity>(
            Ez.Handball.Infrastructure.Tables.Config,
            "PartitionKey eq 'fantasy-v1'",
            default), Times.Once);
    }

    [Fact]
    public async Task GetAsync_FantasyV2_IncludesHbStatzPoints()
    {
        SetupGroup("fantasy-v2", FullFantasyV2);

        var rs = await CreateSut().GetAsync(GameFlavor.Fantasy, 2, default);

        Assert.NotNull(rs);
        Assert.Equal(1, rs!.AssistPoints);
        Assert.Equal(1, rs.StealPoints);
        Assert.Equal(1, rs.BlockPoints);
        Assert.Equal(0.5, rs.SavePoints);
    }

    [Fact]
    public async Task GetAsync_MissingGroup_ReturnsNull()
    {
        SetupGroup("fantasy-v9"); // no rows

        Assert.Null(await CreateSut().GetAsync(GameFlavor.Fantasy, 9, default));
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        SetupGroup("fantasy-v1", ("goals", "2"), ("yellowCards", "-1")); // incomplete

        Assert.Null(await CreateSut().GetAsync(GameFlavor.Fantasy, 1, default));
    }

    [Fact]
    public async Task GetAsync_UnparseableValue_ReturnsNull()
    {
        SetupGroup("fantasy-v1",
            ("goals", "abc"), ("yellowCards", "-1"), ("twoMinute", "-2"), ("redCards", "-5"), ("appearances", "1"));

        Assert.Null(await CreateSut().GetAsync(GameFlavor.Fantasy, 1, default));
    }

    [Fact]
    public async Task GetAsync_ComposesLowercasedGroupName_ForManager()
    {
        SetupGroup("manager-v2",
            ("goals", "0"), ("yellowCards", "0"), ("twoMinute", "0"), ("redCards", "0"), ("appearances", "0"));

        var rs = await CreateSut().GetAsync(GameFlavor.Manager, 2, default);

        Assert.NotNull(rs);
        _query.Verify(q => q.QueryAsync<ConfigEntity>(
            Ez.Handball.Infrastructure.Tables.Config,
            "PartitionKey eq 'manager-v2'",
            default), Times.Once);
    }
}
