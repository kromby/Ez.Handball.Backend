using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedLineupConstraintsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SeedLineupConstraintsFunction CreateSut() => new(_tableWriter.Object);

    [Fact]
    public void Definitions_AreTheFantasyLineupV1Group()
    {
        var defs = SeedLineupConstraintsFunction.ConstraintDefinitions;

        Assert.All(defs, d => Assert.Equal("fantasy-lineup-v1", d.Group));
        Assert.Contains(defs, d => d.Key == "starterCount" && d.Value == "7");
        Assert.Contains(defs, d => d.Key == "captainMultiplier" && d.Value == "2");
        Assert.Contains(defs, d => d.Key == "startMin:GK" && d.Value == "1");
        Assert.Contains(defs, d => d.Key == "startMax:GK" && d.Value == "1");
    }

    [Fact]
    public async Task ProcessAsync_UpsertsEveryRow_IntoConfigTable()
    {
        var seeded = await CreateSut().ProcessAsync();

        Assert.Equal(SeedLineupConstraintsFunction.ConstraintDefinitions.Count, seeded);

        // Verify each definition maps to the right Config row (Group/Key/Value), not just the count.
        foreach (var def in SeedLineupConstraintsFunction.ConstraintDefinitions)
        {
            _tableWriter.Verify(t => t.UpsertAsync(
                "Config",
                It.Is<ConfigEntity>(e =>
                    e.PartitionKey == def.Group && e.RowKey == def.Key && e.Value == def.Value),
                default,
                Azure.Data.Tables.TableUpdateMode.Replace),
                Times.Once);
        }
    }
}
