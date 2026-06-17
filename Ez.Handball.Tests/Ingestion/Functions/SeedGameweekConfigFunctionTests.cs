using Ez.Handball.Ingestion.Functions;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedGameweekConfigFunctionTests
{
    [Fact]
    public void ConfigDefinitions_IncludeMatchFinalBufferHours()
    {
        Assert.Contains(
            ("fantasy-gameweek-v1", "matchFinalBufferHours", "3"),
            SeedGameweekConfigFunction.ConfigDefinitions);
    }
}
