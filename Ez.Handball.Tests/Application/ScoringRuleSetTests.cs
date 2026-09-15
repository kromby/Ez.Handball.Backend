using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application;

public class ScoringRuleSetTests
{
    [Fact]
    public void Name_IsLowercasedFlavorAndVersion()
    {
        var rs = new ScoringRuleSet(GameFlavor.Fantasy, 1, 2, -1, -2, -5, 1);

        Assert.Equal("fantasy-v1", rs.Name);
    }

    [Fact]
    public void Name_ManagerFlavor()
    {
        var rs = new ScoringRuleSet(GameFlavor.Manager, 3, 0, 0, 0, 0, 0);

        Assert.Equal("manager-v3", rs.Name);
    }

    [Fact]
    public void HbStatzPoints_DefaultToZero_WhenOmitted()
    {
        var rs = new ScoringRuleSet(GameFlavor.Fantasy, 1, 2, -1, -2, -5, 1);

        Assert.Equal(0, rs.AssistPoints);
        Assert.Equal(0, rs.StealPoints);
        Assert.Equal(0, rs.BlockPoints);
        Assert.Equal(0, rs.SavePoints);
    }

    [Fact]
    public void Name_FantasyV2_WithHbStatzPoints()
    {
        var rs = new ScoringRuleSet(GameFlavor.Fantasy, 2, 2, -1, -2, -5, 1,
            AssistPoints: 1, StealPoints: 1, BlockPoints: 1, SavePoints: 0.5);

        Assert.Equal("fantasy-v2", rs.Name);
        Assert.Equal(1, rs.AssistPoints);
        Assert.Equal(1, rs.StealPoints);
        Assert.Equal(1, rs.BlockPoints);
        Assert.Equal(0.5, rs.SavePoints);
    }
}
