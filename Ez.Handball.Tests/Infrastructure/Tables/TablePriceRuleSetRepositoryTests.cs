using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TablePriceRuleSetRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private void Rows(params (string Key, string Value)[] rows) =>
        _query.Setup(q => q.QueryAsync<ConfigEntity>(
                Ez.Handball.Infrastructure.Tables.Config, It.Is<string>(f => f.Contains("fantasy-price-v1")), It.IsAny<CancellationToken>()))
              .Returns(ToAsync(rows.Select(r => new ConfigEntity
              {
                  PartitionKey = "fantasy-price-v1", RowKey = r.Key, Value = r.Value
              })));

    private static async IAsyncEnumerable<ConfigEntity> ToAsync(IEnumerable<ConfigEntity> items)
    {
        foreach (var i in items) { yield return i; }
        await Task.CompletedTask;
    }

    private TablePriceRuleSetRepository CreateSut() => new(_query.Object);

    [Fact]
    public async Task Get_AssemblesSortedRuleSet()
    {
        Rows(
            ("minGames", "3"),
            ("currency", "ISK"),
            ("band:6", "20000000"),
            ("band:0", "5000000"),
            ("band:3", "10000000"));

        var result = await CreateSut().GetAsync(1, default);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Version);
        Assert.Equal(3, result.MinGames);
        Assert.Equal("ISK", result.Currency);
        Assert.Equal(new[] { 0d, 3d, 6d }, result.Bands.Select(b => b.Threshold));
        Assert.Equal(5000000, result.Bands[0].Price);
        Assert.Equal("fantasy-price-v1", result.Name);
    }

    [Fact]
    public async Task Get_EmptyGroup_ReturnsNull()
    {
        Rows();
        Assert.Null(await CreateSut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_MissingMinGames_ReturnsNull()
    {
        Rows(("currency", "ISK"), ("band:0", "5000000"));
        Assert.Null(await CreateSut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_MissingCurrency_ReturnsNull()
    {
        Rows(("minGames", "3"), ("band:0", "5000000"));
        Assert.Null(await CreateSut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_NoBands_ReturnsNull()
    {
        Rows(("minGames", "3"), ("currency", "ISK"));
        Assert.Null(await CreateSut().GetAsync(1, default));
    }
}
