using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedPriceRuleSetsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SeedPriceRuleSetsFunction CreateSut() => new(_tableWriter.Object);

    [Fact]
    public void Definitions_AreTheFantasyPriceV1Group()
    {
        var defs = SeedPriceRuleSetsFunction.RuleSetDefinitions;

        Assert.All(defs, d => Assert.Equal("fantasy-price-v1", d.Group));
        Assert.Contains(defs, d => d.Key == "minGames" && d.Value == "3");
        Assert.Contains(defs, d => d.Key == "currency" && d.Value == "ISK");
        Assert.Contains(defs, d => d.Key == "band:0" && d.Value == "5000000");
        Assert.Contains(defs, d => d.Key == "band:12" && d.Value == "50000000");
    }

    [Fact]
    public async Task ProcessAsync_UpsertsEveryRow_IntoConfigTable()
    {
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
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-price-v1" && e.RowKey == "band:6" && e.Value == "20000000"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Once);
    }
}
