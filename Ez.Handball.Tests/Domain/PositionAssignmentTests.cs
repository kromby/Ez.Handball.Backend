using Ez.Handball.Domain;
using Xunit;

namespace Ez.Handball.Tests.Domain;

public class PositionAssignmentTests
{
    private static PositionCandidate P(string id, params string[] eligible) => new(id, eligible);

    private static Dictionary<string, (int Min, int Max)> Limits(params (string Pos, int Min, int Max)[] l)
        => l.ToDictionary(x => x.Pos, x => (x.Min, x.Max));

    [Theory]
    [InlineData("CB", "LB", new[] { "CB", "LB" })]
    [InlineData("CB", "", new[] { "CB" })]
    [InlineData("CB", "  ", new[] { "CB" })]
    [InlineData("CB", null, new[] { "CB" })]
    [InlineData("CB", "CB", new[] { "CB" })]
    [InlineData(null, "LB", new string[0])]
    [InlineData("", "LB", new string[0])]
    public void EligibleFor_PrimaryFirst_DropsEmptyOrDuplicateSecondary(string? pos, string? sec, string[] expected)
        => Assert.Equal(expected, PositionAssignment.EligibleFor(pos, sec));

    [Fact]
    public void SinglePositionPlayers_WithinMax_Solves()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB"), P("b", "CB") }, Limits(("CB", 0, 2)));

        Assert.NotNull(result);
        Assert.Equal("CB", result!["a"]);
        Assert.Equal("CB", result["b"]);
    }

    [Fact]
    public void SinglePositionPlayers_OverMax_IsInfeasible()
        => Assert.Null(PositionAssignment.TrySolve(
            new[] { P("a", "CB"), P("b", "CB"), P("c", "CB") }, Limits(("CB", 0, 2))));

    [Fact]
    public void DualPlayer_FillsSecondary_WhenPrimaryFull()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("b", "CB"), P("a", "CB", "LB") }, Limits(("CB", 0, 1), ("LB", 0, 1)));

        Assert.Equal("CB", result!["b"]);
        Assert.Equal("LB", result["a"]);
    }

    [Fact]
    public void GreedyTrap_DualPlayerFirst_StillSolvesByBacktracking()
    {
        // A tries CB first (primary), which would strand B; backtracking moves A to LB.
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB", "LB"), P("b", "CB") }, Limits(("CB", 0, 1), ("LB", 0, 1)));

        Assert.Equal("LB", result!["a"]);
        Assert.Equal("CB", result["b"]);
    }

    [Fact]
    public void PrimaryPreferred_WhenBothFit()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB", "LB") }, Limits(("CB", 0, 2), ("LB", 0, 2)));

        Assert.Equal("CB", result!["a"]);
    }

    [Fact]
    public void Min_MetBySecondary()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB", "GK"), P("b", "LW") }, Limits(("GK", 1, 1), ("CB", 0, 2), ("LW", 0, 2)));

        Assert.Equal("GK", result!["a"]);
    }

    [Fact]
    public void Min_Unmet_IsInfeasible()
        => Assert.Null(PositionAssignment.TrySolve(
            new[] { P("a", "CB"), P("b", "LW") }, Limits(("GK", 1, 1), ("CB", 0, 2), ("LW", 0, 2))));

    [Fact]
    public void UnknownPosition_IsUnconstrained_AssignedNull()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a"), P("b", "CB") }, Limits(("CB", 0, 1)));

        Assert.Null(result!["a"]);
        Assert.Equal("CB", result["b"]);
    }

    [Fact]
    public void PositionWithoutLimit_IsUnconstrained()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "FP"), P("b", "FP"), P("c", "FP") }, Limits(("CB", 0, 1)));

        Assert.Equal("FP", result!["c"]);
    }

    [Fact]
    public void EmptyPlayers_WithZeroMins_Solves()
        => Assert.NotNull(PositionAssignment.TrySolve(Array.Empty<PositionCandidate>(), Limits(("CB", 0, 1))));

    [Fact]
    public void FifteenDualPlayers_Infeasible_ReturnsQuickly()
    {
        var players = Enumerable.Range(0, 15).Select(i => P($"p{i}", "CB", "LB")).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = PositionAssignment.TrySolve(players, Limits(("CB", 0, 3), ("LB", 0, 3)));

        Assert.Null(result);
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void SameInput_SameAssignment()
    {
        var players = new[] { P("a", "CB", "LB"), P("b", "CB", "RB"), P("c", "CB") };
        var limits = Limits(("CB", 0, 1), ("LB", 0, 1), ("RB", 0, 1));

        Assert.Equal(PositionAssignment.TrySolve(players, limits), PositionAssignment.TrySolve(players, limits));
    }

    [Fact]
    public void MaxOnly_ZeroesMins()
    {
        var max = PositionAssignment.MaxOnly(Limits(("GK", 1, 1), ("CB", 0, 2)));
        Assert.Equal((0, 1), max["GK"]);
        Assert.Equal((0, 2), max["CB"]);

        var fromCaps = PositionAssignment.MaxOnly(new Dictionary<string, int> { ["LB"] = 3 });
        Assert.Equal((0, 3), fromCaps["LB"]);
    }
}
