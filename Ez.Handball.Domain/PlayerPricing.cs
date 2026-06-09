namespace Ez.Handball.Domain;

public sealed record PlayerPricing(
    string PlayerId,
    PlayerPrice Price,
    double Score,    // points per game (0 when below the min-games guard)
    int Games,
    string Version,  // the price rule set name, e.g. "fantasy-price-v1"
    double Rating);  // current-season fantasy rating (the #52 metric)
