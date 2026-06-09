namespace Ez.Handball.Domain;

public sealed record PriceBand(double Threshold, double Price);

public sealed record PriceRuleSet(
    int Version,
    int MinGames,
    string Currency,
    IReadOnlyList<PriceBand> Bands)   // sorted ascending by Threshold; non-empty
{
    public string Name => $"fantasy-price-v{Version}";

    // Highest band whose threshold <= score; the floor band when below the lowest.
    public PriceBand BandFor(double score) =>
        Bands.LastOrDefault(b => b.Threshold <= score) ?? Bands[0];
}
