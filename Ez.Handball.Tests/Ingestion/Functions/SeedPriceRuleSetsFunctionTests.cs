using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedPriceRuleSetsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SeedPriceRuleSetsFunction CreateSut() => new(_tableWriter.Object);

    private void ExistingRowsAre(params string[] rowKeys) =>
        _tableWriter.Setup(t => t.QueryAsync<ConfigEntity>("Config", "PartitionKey eq 'fantasy-price-v1'", default))
            .ReturnsAsync(rowKeys.Select(k => new ConfigEntity { PartitionKey = "fantasy-price-v1", RowKey = k }).ToList());

    [Fact]
    public void Definitions_AreTheFantasyPriceV1Group()
    {
        var defs = SeedPriceRuleSetsFunction.RuleSetDefinitions;

        Assert.All(defs, d => Assert.Equal("fantasy-price-v1", d.Group));
        Assert.Contains(defs, d => d.Key == "minGames" && d.Value == "3");
        Assert.Contains(defs, d => d.Key == "blendGames" && d.Value == "10");
        Assert.Contains(defs, d => d.Key == "currency" && d.Value == "ISK");
    }

    [Fact]
    public void Bands_Run5MTo35MIn2MSteps_Every1Point2PointsPerGame()
    {
        var bands = SeedPriceRuleSetsFunction.RuleSetDefinitions
            .Where(d => d.Key.StartsWith("band:"))
            .Select(d => (Threshold: decimal.Parse(d.Key["band:".Length..], System.Globalization.CultureInfo.InvariantCulture),
                          Price: int.Parse(d.Value)))
            .OrderBy(b => b.Threshold)
            .ToList();

        Assert.Equal(16, bands.Count);
        Assert.Equal((0m, 5_000_000), bands[0]);
        Assert.Equal((3.2m, 7_000_000), bands[1]);
        Assert.Equal((20m, 35_000_000), bands[^1]);
        for (var i = 1; i < bands.Count; i++)
        {
            Assert.Equal(2_000_000, bands[i].Price - bands[i - 1].Price);
            Assert.Equal(i == 1 ? 3.2m : 1.2m, bands[i].Threshold - bands[i - 1].Threshold);
        }
    }

    [Fact]
    public async Task ProcessAsync_UpsertsEveryRow_IntoConfigTable()
    {
        ExistingRowsAre();

        var seeded = await CreateSut().ProcessAsync();

        Assert.Equal(SeedPriceRuleSetsFunction.RuleSetDefinitions.Count, seeded);

        _tableWriter.Verify(t => t.UpsertAsync(
            "Config",
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-price-v1"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Exactly(SeedPriceRuleSetsFunction.RuleSetDefinitions.Count));

        _tableWriter.Verify(t => t.UpsertAsync(
            "Config",
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-price-v1" && e.RowKey == "band:6.8" && e.Value == "13000000"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DeletesStaleBandRows_ButKeepsCurrentBandsAndOtherKeys()
    {
        // A leftover band (e.g. the old "40M from 13 ppg") would otherwise override the new steps.
        ExistingRowsAre("band:0", "band:12", "band:13", "minGames", "someOtherKey");

        await CreateSut().ProcessAsync();

        _tableWriter.Verify(t => t.DeleteAsync("Config", "fantasy-price-v1", "band:12", default), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync("Config", "fantasy-price-v1", "band:13", default), Times.Once);
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), "band:0", It.IsAny<CancellationToken>()), Times.Never);
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), "minGames", It.IsAny<CancellationToken>()), Times.Never);
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), "someOtherKey", It.IsAny<CancellationToken>()), Times.Never);
    }
}
