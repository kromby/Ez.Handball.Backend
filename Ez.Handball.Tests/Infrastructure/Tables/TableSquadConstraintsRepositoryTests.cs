using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableSquadConstraintsRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private void Rows(params (string Key, string Value)[] rows) =>
        _query.Setup(q => q.QueryAsync<ConfigEntity>(
                Ez.Handball.Infrastructure.Tables.Config,
                It.Is<string>(f => f.Contains("fantasy-squad-v1")), It.IsAny<CancellationToken>()))
              .Returns(ToAsync(rows.Select(r => new ConfigEntity
              {
                  PartitionKey = "fantasy-squad-v1", RowKey = r.Key, Value = r.Value
              })));

    private static async IAsyncEnumerable<ConfigEntity> ToAsync(IEnumerable<ConfigEntity> items)
    {
        foreach (var i in items) { yield return i; }
        await Task.CompletedTask;
    }

    private TableSquadConstraintsRepository CreateSut() => new(_query.Object);

    [Fact]
    public async Task Get_AssemblesConstraints()
    {
        Rows(
            ("startingCap", "100000000"),
            ("currency", "ISK"),
            ("maxSquadSize", "15"),
            ("posLimit:GK", "2"),
            ("posLimit:Back", "5"));

        var result = await CreateSut().GetAsync(1, default);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Version);
        Assert.Equal(15, result.MaxSquadSize);
        Assert.Equal(100000000, result.StartingCap);
        Assert.Equal("ISK", result.Currency);
        Assert.Equal(2, result.PositionLimits["GK"]);
        Assert.Equal(5, result.PositionLimits["Back"]);
    }

    [Fact]
    public async Task Get_NoPositionLimits_IsAllowed()
    {
        Rows(("startingCap", "100000000"), ("currency", "ISK"), ("maxSquadSize", "15"));

        var result = await CreateSut().GetAsync(1, default);

        Assert.NotNull(result);
        Assert.Empty(result!.PositionLimits);
    }

    [Fact]
    public async Task Get_EmptyGroup_ReturnsNull()
    {
        Rows();
        Assert.Null(await CreateSut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_MissingStartingCap_ReturnsNull()
    {
        Rows(("currency", "ISK"), ("maxSquadSize", "15"));
        Assert.Null(await CreateSut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_MissingMaxSquadSize_ReturnsNull()
    {
        Rows(("startingCap", "100000000"), ("currency", "ISK"));
        Assert.Null(await CreateSut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_MissingCurrency_ReturnsNull()
    {
        Rows(("startingCap", "100000000"), ("maxSquadSize", "15"));
        Assert.Null(await CreateSut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_ParsesSellOnFeeRate_AndDefaultsToHalfWhenAbsent()
    {
        // Arrange: seed a fantasy-squad-v1 group WITHOUT sellOnFeeRate, then read.
        Rows(
            ("startingCap", "100000000"),
            ("currency", "ISK"),
            ("maxSquadSize", "15"));

        var withoutFee = await CreateSut().GetAsync(1, default);
        Assert.Equal(0.5, withoutFee!.SellOnFeeRate);

        // Arrange: add sellOnFeeRate = 0.25 to the same group, then read.
        Rows(
            ("startingCap", "100000000"),
            ("currency", "ISK"),
            ("maxSquadSize", "15"),
            ("sellOnFeeRate", "0.25"));

        var withFee = await CreateSut().GetAsync(1, default);
        Assert.Equal(0.25, withFee!.SellOnFeeRate);
    }
}
