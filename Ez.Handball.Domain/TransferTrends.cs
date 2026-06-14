namespace Ez.Handball.Domain;

public sealed record TransferTrendEntry(
    string PlayerId,
    string? Name,
    string? ClubName,
    int Count);

public sealed record TransferTrends(
    IReadOnlyList<TransferTrendEntry> MostSigned,
    IReadOnlyList<TransferTrendEntry> MostDropped);
