using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class SellValueTests
{
    [Fact] // profit: keep PricePaid + floor(profit * (1 - feeRate))
    public void Profit_KeepsHalfTheProfit_Floored()
    {
        // paid 40M, now 51M, fee 0.5 => 40M + floor(11M * 0.5) = 40M + 5_500_000
        Assert.Equal(45_500_000, SellValue.Compute(40_000_000, 51_000_000, 0.5));
    }

    [Fact] // floor applies to odd profit splits
    public void Profit_FloorsOddSplit()
    {
        // paid 0, now 5, fee 0.5 => 0 + floor(2.5) = 2
        Assert.Equal(2, SellValue.Compute(0, 5, 0.5));
    }

    [Fact] // loss: full current value (no protection)
    public void Loss_ReturnsCurrentValue()
    {
        Assert.Equal(30_000_000, SellValue.Compute(40_000_000, 30_000_000, 0.5));
    }

    [Fact] // break-even: returns the price paid
    public void BreakEven_ReturnsPricePaid()
    {
        Assert.Equal(40_000_000, SellValue.Compute(40_000_000, 40_000_000, 0.5));
    }

    [Fact] // feeRate 0 => keep all profit
    public void ZeroFee_KeepsAllProfit()
    {
        Assert.Equal(51_000_000, SellValue.Compute(40_000_000, 51_000_000, 0.0));
    }
}
