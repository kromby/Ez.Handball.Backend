using Ez.Handball.Application.BuyFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.BuyFunctions;

public class FantasyBuyPlayerFunctionTests
{
    private static readonly PlayerPrice Cost = new(20_000_000, "ISK");

    private static SquadConstraints Constraints(int maxSize = 15, IReadOnlyDictionary<string, int>? posLimits = null) =>
        new(1, maxSize, posLimits ?? new Dictionary<string, int>(), 100_000_000, "ISK");

    private static Squad Squad(double budget, params SquadSlot[] players) =>
        new(players, budget, "ISK");

    private static SquadSlot Slot(string id, string? pos = "Back") =>
        new(id, pos, new PlayerPrice(10_000_000, "ISK"));

    private static BuyPlayerInputs Inputs(Squad squad, SquadConstraints constraints, string? position = "Back") =>
        new("p1", position, Cost, "fantasy-price-v1", constraints, squad, new BuyPlayerContext(null, null, null));

    private readonly FantasyBuyPlayerFunction _sut = new();

    [Fact]
    public void Flavor_IsFantasy() => Assert.Equal(GameFlavor.Fantasy, _sut.Flavor);

    [Fact]
    public void AllRulesPass_AllowedWithNoViolations()
    {
        var result = _sut.Evaluate(Inputs(Squad(100_000_000), Constraints()));

        Assert.True(result.Allowed);
        Assert.Empty(result.Violations);
        Assert.Equal("p1", result.PlayerId);
        Assert.Equal("fantasy", result.Flavor);
        Assert.Equal(Cost, result.Cost);
        Assert.Equal("fantasy-price-v1", result.Version);
    }

    [Fact]
    public void CostExceedsBudget_InsufficientBudget()
    {
        var result = _sut.Evaluate(Inputs(Squad(budget: 10_000_000), Constraints()));

        Assert.False(result.Allowed);
        Assert.Contains(result.Violations, v => v.Code == "insufficient_budget");
    }

    [Fact]
    public void SquadAtMaxSize_SquadFull()
    {
        var full = Enumerable.Range(0, 3).Select(i => Slot($"x{i}")).ToArray();
        var result = _sut.Evaluate(Inputs(Squad(100_000_000, full), Constraints(maxSize: 3)));

        Assert.False(result.Allowed);
        Assert.Contains(result.Violations, v => v.Code == "squad_full");
    }

    [Fact]
    public void PlayerAlreadyOwned_DuplicatePlayer()
    {
        var result = _sut.Evaluate(Inputs(Squad(100_000_000, Slot("p1")), Constraints()));

        Assert.False(result.Allowed);
        Assert.Contains(result.Violations, v => v.Code == "duplicate_player");
    }

    [Fact]
    public void PositionAtLimit_PositionLimit()
    {
        var limits = new Dictionary<string, int> { ["Back"] = 1 };
        var result = _sut.Evaluate(Inputs(Squad(100_000_000, Slot("x1", "Back")), Constraints(posLimits: limits)));

        Assert.False(result.Allowed);
        Assert.Contains(result.Violations, v => v.Code == "position_limit");
    }

    [Fact]
    public void PositionWithNoConfiguredLimit_NoPositionViolation()
    {
        var result = _sut.Evaluate(Inputs(Squad(100_000_000, Slot("x1", "Wing")), Constraints(), position: "Wing"));

        Assert.DoesNotContain(result.Violations, v => v.Code == "position_limit");
    }

    [Fact]
    public void MultipleRulesFail_AllReported()
    {
        var limits = new Dictionary<string, int> { ["Back"] = 1 };
        var squad = Squad(budget: 0, Slot("p1", "Back"));   // over budget + duplicate + position full
        var result = _sut.Evaluate(Inputs(squad, Constraints(maxSize: 1, posLimits: limits)));

        Assert.False(result.Allowed);
        var codes = result.Violations.Select(v => v.Code).ToHashSet();
        Assert.Contains("insufficient_budget", codes);
        Assert.Contains("squad_full", codes);
        Assert.Contains("duplicate_player", codes);
        Assert.Contains("position_limit", codes);
    }
}
