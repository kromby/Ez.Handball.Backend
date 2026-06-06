using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedScoringRuleSetsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SeedScoringRuleSetsFunction CreateSut() => new(_tableWriter.Object);

    [Fact]
    public void RuleSetDefinitions_AreTheFantasyV1Weights()
    {
        var defs = SeedScoringRuleSetsFunction.RuleSetDefinitions;

        Assert.Equal(5, defs.Count);
        Assert.All(defs, d => Assert.Equal("fantasy-v1", d.Group));
        Assert.Contains(defs, d => d.Key == "goals" && d.Value == "2");
        Assert.Contains(defs, d => d.Key == "yellowCards" && d.Value == "-1");
        Assert.Contains(defs, d => d.Key == "twoMinute" && d.Value == "-2");
        Assert.Contains(defs, d => d.Key == "redCards" && d.Value == "-5");
        Assert.Contains(defs, d => d.Key == "appearances" && d.Value == "1");
    }

    [Fact]
    public async Task ProcessAsync_UpsertsEveryRuleSetRow_IntoConfigTable()
    {
        var seeded = await CreateSut().ProcessAsync();

        Assert.Equal(SeedScoringRuleSetsFunction.RuleSetDefinitions.Count, seeded);

        _tableWriter.Verify(t => t.UpsertAsync(
            "Config",
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-v1"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Exactly(SeedScoringRuleSetsFunction.RuleSetDefinitions.Count));

        _tableWriter.Verify(t => t.UpsertAsync(
            "Config",
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-v1" && e.RowKey == "goals" && e.Value == "2"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Once);
    }
}
