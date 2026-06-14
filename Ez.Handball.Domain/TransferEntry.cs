namespace Ez.Handball.Domain;

// One immutable buy/sell event. Cost = amount deducted on a buy, credit received on a sell.
public sealed record TransferEntry(
    string UserId,
    string PlayerId,
    GameFlavor Flavor,
    TransferType Type,
    double Cost,
    string? SeasonLabel,
    DateTimeOffset CreatedAt);
