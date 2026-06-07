namespace Ez.Handball.Domain;

public sealed record SalaryBand(double Threshold, double Price);

public sealed record SalaryRuleSet(
    int Version,
    int MinGames,
    string Currency,
    IReadOnlyList<SalaryBand> Bands)   // sorted ascending by Threshold; non-empty
{
    public string Name => $"fantasy-price-v{Version}";

    // Highest band whose threshold <= score; the floor band when below the lowest.
    public SalaryBand BandFor(double score) =>
        Bands.LastOrDefault(b => b.Threshold <= score) ?? Bands[0];
}
