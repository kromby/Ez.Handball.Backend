namespace Ez.Handball.Domain;

// FPL-style sell value. On a rise, the house keeps `feeRate` of the profit (you keep the
// rest, floored to whole units). On a fall (or break-even), you realise the full current
// price. Pure: no I/O.
public static class SellValue
{
    public static double Compute(double pricePaid, double current, double feeRate)
    {
        if (current <= pricePaid) return current;
        var profit = current - pricePaid;
        return pricePaid + Math.Floor(profit * (1 - feeRate));
    }
}
