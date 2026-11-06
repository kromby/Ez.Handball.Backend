using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedPriceRuleSetsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private readonly List<IReadOnlyList<TableTransactionAction>> _submitted = [];

    public SeedPriceRuleSetsFunctionTests() =>
        _tableWriter.Setup(t => t.SubmitTransactionAsync("Config", It.IsAny<IReadOnlyList<TableTransactionAction>>(), default))
            .Callback<string, IReadOnlyList<TableTransactionAction>, CancellationToken>((_, actions, _) => _submitted.Add(actions))
            .Returns(Task.CompletedTask);

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
    public async Task ProcessAsync_UpsertsEveryRow_InOneTransaction()
    {
        ExistingRowsAre();

        var seeded = await CreateSut().ProcessAsync();

        Assert.Equal(SeedPriceRuleSetsFunction.RuleSetDefinitions.Count, seeded);
        var actions = Assert.Single(_submitted);
        Assert.Equal(SeedPriceRuleSetsFunction.RuleSetDefinitions.Count, actions.Count);
        Assert.All(actions, a =>
        {
            Assert.Equal(TableTransactionActionType.UpsertReplace, a.ActionType);
            Assert.Equal("fantasy-price-v1", a.Entity.PartitionKey);
        });
        var band = (ConfigEntity)actions.Single(a => a.Entity.RowKey == "band:6.8").Entity;
        Assert.Equal("13000000", band.Value);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<ConfigEntity>(), It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DeletesEveryStaleBand_InTheSameTransaction_ButKeepsCurrentBandsAndOtherKeys()
    {
        // The old production steps (3/6/9/13) and the old seed's band:12. A leftover band would
        // otherwise override the new steps.
        ExistingRowsAre("band:0", "band:3", "band:6", "band:9", "band:12", "band:13", "minGames", "someOtherKey");

        await CreateSut().ProcessAsync();

        var actions = Assert.Single(_submitted);
        var deleted = actions.Where(a => a.ActionType == TableTransactionActionType.Delete)
                             .Select(a => a.Entity.RowKey).Order().ToList();
        Assert.Equal(["band:12", "band:13", "band:3", "band:6", "band:9"], deleted);
        Assert.All(actions.Where(a => a.ActionType == TableTransactionActionType.Delete),
            a => Assert.Equal("fantasy-price-v1", a.Entity.PartitionKey));
        _tableWriter.Verify(t => t.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
