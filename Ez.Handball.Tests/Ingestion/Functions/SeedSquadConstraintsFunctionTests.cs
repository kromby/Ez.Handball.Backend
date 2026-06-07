using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedSquadConstraintsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SeedSquadConstraintsFunction CreateSut() => new(_tableWriter.Object);

    [Fact]
    public void Definitions_AreTheFantasySquadV1Group()
    {
        var defs = SeedSquadConstraintsFunction.ConstraintDefinitions;

        Assert.All(defs, d => Assert.Equal("fantasy-squad-v1", d.Group));
        Assert.Contains(defs, d => d.Key == "startingCap" && d.Value == "100000000");
        Assert.Contains(defs, d => d.Key == "currency" && d.Value == "ISK");
        Assert.Contains(defs, d => d.Key == "maxSquadSize" && d.Value == "15");
        Assert.Contains(defs, d => d.Key.StartsWith("posLimit:"));
    }

    [Fact]
    public async Task ProcessAsync_UpsertsEveryRow_IntoConfigTable()
    {
        var seeded = await CreateSut().ProcessAsync();

        Assert.Equal(SeedSquadConstraintsFunction.ConstraintDefinitions.Count, seeded);

        _tableWriter.Verify(t => t.UpsertAsync(
            "Config",
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-squad-v1"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Exactly(SeedSquadConstraintsFunction.ConstraintDefinitions.Count));

        _tableWriter.Verify(t => t.UpsertAsync(
            "Config",
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-squad-v1" && e.RowKey == "maxSquadSize" && e.Value == "15"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Once);
    }
}
