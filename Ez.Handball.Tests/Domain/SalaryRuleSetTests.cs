using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class SalaryRuleSetTests
{
    private static SalaryRuleSet Rs() => new(1, 3, "ISK", new[]
    {
        new SalaryBand(0, 5000000),
        new SalaryBand(3, 10000000),
        new SalaryBand(6, 20000000),
    });

    [Fact]
    public void Name_IsFantasyPriceVersion()
    {
        Assert.Equal("fantasy-price-v1", Rs().Name);
    }

    [Fact]
    public void BandFor_PicksHighestThresholdAtOrBelowScore()
    {
        Assert.Equal(20000000, Rs().BandFor(7).Price);   // 7 -> band 6
        Assert.Equal(10000000, Rs().BandFor(3).Price);   // exact threshold 3
        Assert.Equal(5000000, Rs().BandFor(2).Price);    // between 0 and 3 -> band 0
    }

    [Fact]
    public void BandFor_BelowLowestThreshold_ReturnsFloorBand()
    {
        Assert.Equal(5000000, Rs().BandFor(-1).Price);   // below lowest -> floor band
    }
}
