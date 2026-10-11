using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class SquadPlayerEligibilityTests
{
    private static SquadPlayer Dual(string? playAs) => new(
        PlayerId: "p", Name: "p", ClubId: "1", ClubName: "Club", Position: "CB", Gender: "karlar",
        Price: new PlayerPrice(0, "ISK"), PricePaid: new PlayerPrice(0, "ISK"),
        Rating: 0, PositionSecondary: "LB", PlayAs: playAs);

    [Fact]
    public void NoPin_EligibleIsBothNaturalPositions()
    {
        var p = Dual(null);
        Assert.Equal(new[] { "CB", "LB" }, p.EligiblePositions);
        Assert.Null(p.ValidPlayAs);
    }

    [Fact]
    public void PinToSecondary_NarrowsToThatPosition()
    {
        var p = Dual("LB");
        Assert.Equal(new[] { "LB" }, p.EligiblePositions);
        Assert.Equal("LB", p.ValidPlayAs);
    }

    [Fact]
    public void PinToPrimary_NarrowsToThatPosition()
    {
        Assert.Equal(new[] { "CB" }, Dual("CB").EligiblePositions);
    }

    [Fact]
    public void StalePin_FallsBackToNatural()
    {
        var p = Dual("RW");
        Assert.Equal(new[] { "CB", "LB" }, p.EligiblePositions);
        Assert.Null(p.ValidPlayAs);
    }

    [Fact]
    public void BlankPin_FallsBackToNatural()
    {
        var p = Dual("  ");
        Assert.Equal(new[] { "CB", "LB" }, p.NaturalPositions);
        Assert.Equal(new[] { "CB", "LB" }, p.EligiblePositions);
        Assert.Null(p.ValidPlayAs);
    }
}
