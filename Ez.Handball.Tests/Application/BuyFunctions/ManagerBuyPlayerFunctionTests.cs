using Ez.Handball.Application.BuyFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.BuyFunctions;

public class ManagerBuyPlayerFunctionTests
{
    private static BuyPlayerInputs Inputs() =>
        new("p7", "Back", new PlayerCost(999, "ISK"), "ignored",
            new SquadConstraints(0, 0, new Dictionary<string, int>(), 0, "ISK"),
            new Squad(System.Array.Empty<SquadSlot>(), 0, "ISK"),
            new BuyPlayerContext(null, null, null));

    private readonly ManagerBuyPlayerFunction _sut = new();

    [Fact]
    public void Flavor_IsManager() => Assert.Equal(GameFlavor.Manager, _sut.Flavor);

    [Fact]
    public void Evaluate_IsDeterministicAllowedStub()
    {
        var result = _sut.Evaluate(Inputs());

        Assert.Equal("p7", result.PlayerId);
        Assert.Equal("manager", result.Flavor);
        Assert.True(result.Allowed);
        Assert.Empty(result.Violations);
        Assert.Equal("manager-v0", result.Version);
        Assert.Equal(0, result.Cost.Amount);
        Assert.Equal("ISK", result.Cost.Currency);
    }
}
