using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class RoundOrderTests
{
    [Fact]
    public void Key_OrdersNumericAscending_ThenNonNumericLast()
    {
        var rounds = new[] { "10", "2", "Undanúrslit", "1" };
        var ordered = rounds
            .OrderBy(RoundOrder.Key)
            .ThenBy(r => r, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "1", "2", "10", "Undanúrslit" }, ordered);
    }

    [Fact]
    public void Compare_ReturnsNegative_WhenFirstRoundSortsEarlier()
    {
        Assert.True(RoundOrder.Compare("1", "2") < 0);
        Assert.True(RoundOrder.Compare("10", "Lokaúrslit") < 0); // numeric before text
        Assert.Equal(0, RoundOrder.Compare("3", "3"));
    }
}
