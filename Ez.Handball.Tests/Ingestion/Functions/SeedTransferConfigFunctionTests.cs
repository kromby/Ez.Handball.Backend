using Ez.Handball.Ingestion.Functions;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedTransferConfigFunctionTests
{
    [Theory]
    [InlineData("freeTransfersPerRound", "1")]
    [InlineData("maxBankedFreeTransfers", "1")]
    [InlineData("pointsPerExtraTransfer", "4")]
    public void ConfigDefinitions_SeedTheAgreedDefaults(string key, string value)
    {
        Assert.Contains(("fantasy-transfers-v1", key, value), SeedTransferConfigFunction.ConfigDefinitions);
    }

    [Fact]
    public void ConfigDefinitions_SeedAStartRound()
    {
        Assert.Contains(SeedTransferConfigFunction.ConfigDefinitions,
            d => d is ("fantasy-transfers-v1", "startsFromRound", _));
    }
}
