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
}
