using Ez.Handball.Ingestion.Parsing;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class PositionModeCalculatorTests
{
    private static DateTimeOffset Day(int d) => new(2026, 1, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_SingleObservation_PrimaryIsThatCodeNoSecondary()
    {
        var (primary, secondary) = PositionModeCalculator.Compute(new[] { ("LB", Day(1)) });

        Assert.Equal("LB", primary);
        Assert.Null(secondary);
    }

    [Fact]
    public void Compute_OneDistinctCode_NoSecondaryEvenWithManyObservations()
    {
        var observations = Enumerable.Range(1, 5).Select(d => ("CB", Day(d))).ToList();

        var (primary, secondary) = PositionModeCalculator.Compute(observations);

        Assert.Equal("CB", primary);
        Assert.Null(secondary);
    }

    [Fact]
    public void Compute_SecondCodeAboveTenPercent_IsReturnedAsSecondary()
    {
        // 100 observations: 89 CB, 11 LB -> 11% > 10%
        var observations = Enumerable.Range(1, 89).Select(d => ("CB", Day(1)))
            .Concat(Enumerable.Range(1, 11).Select(d => ("LB", Day(2))))
            .ToList();

        var (primary, secondary) = PositionModeCalculator.Compute(observations);

        Assert.Equal("CB", primary);
        Assert.Equal("LB", secondary);
    }

    [Fact]
    public void Compute_SecondCodeAtExactlyTenPercent_IsNotSecondary()
    {
        // 100 observations: 90 CB, 10 LB -> exactly 10%, rule requires STRICTLY more than 10%
        var observations = Enumerable.Range(1, 90).Select(d => ("CB", Day(1)))
            .Concat(Enumerable.Range(1, 10).Select(d => ("LB", Day(2))))
            .ToList();

        var (primary, secondary) = PositionModeCalculator.Compute(observations);

        Assert.Equal("CB", primary);
        Assert.Null(secondary);
    }

    [Fact]
    public void Compute_TiedPrimaryCounts_EarliestFirstSeenWins()
    {
        var observations = new[]
        {
            ("LB", Day(5)),  // LB first seen day 5
            ("RB", Day(2)),  // RB first seen day 2 -> should win the tie
            ("LB", Day(6)),
            ("RB", Day(7)),
        };

        var (primary, _) = PositionModeCalculator.Compute(observations);

        Assert.Equal("RB", primary);
    }

    [Fact]
    public void Compute_TiedCountsAndTiedFirstSeenDate_BreaksByCodeOrdinal()
    {
        // LB and RB both have count=1 and both first (and only) observed on the same date —
        // count and date alone can't break the tie, so it must fall back to the code itself.
        var observations = new[] { ("RB", Day(3)), ("LB", Day(3)) };

        var (primary, _) = PositionModeCalculator.Compute(observations);

        Assert.Equal("LB", primary); // "LB" < "RB" ordinally
    }

    [Fact]
    public void Compute_EmptyObservations_Throws()
    {
        Assert.Throws<ArgumentException>(() => PositionModeCalculator.Compute(Array.Empty<(string, DateTimeOffset)>()));
    }
}
