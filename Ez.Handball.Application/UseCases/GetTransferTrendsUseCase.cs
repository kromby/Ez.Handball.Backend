using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record TransferTrendsResult
{
    public sealed record Ok(TransferTrends Trends) : TransferTrendsResult;
    public sealed record InvalidWindow : TransferTrendsResult { public static readonly InvalidWindow Instance = new(); }
}

public interface IGetTransferTrendsUseCase
{
    Task<TransferTrendsResult> ExecuteAsync(GameFlavor flavor, string? window, CancellationToken ct);
}

public sealed class GetTransferTrendsUseCase : IGetTransferTrendsUseCase
{
    private const int TopN = 10;

    private readonly ITransferLedgerRepository _ledger;
    private readonly IPlayerRepository _players;
    private readonly Func<DateTimeOffset> _now;

    public GetTransferTrendsUseCase(ITransferLedgerRepository ledger, IPlayerRepository players, Func<DateTimeOffset> now)
    {
        _ledger = ledger;
        _players = players;
        _now = now;
    }

    public async Task<TransferTrendsResult> ExecuteAsync(GameFlavor flavor, string? window, CancellationToken ct)
    {
        var span = ParseWindow(window);
        if (span is null) return TransferTrendsResult.InvalidWindow.Instance;

        var now = _now();
        var entries = await _ledger.ListSinceAsync(flavor, now - span.Value, now, ct);

        var signed = TopByType(entries, TransferType.Buy);
        var dropped = TopByType(entries, TransferType.Sell);

        var players = await LoadPlayersAsync(signed.Concat(dropped).Select(x => x.PlayerId), ct);

        return new TransferTrendsResult.Ok(new TransferTrends(
            signed.Select(x => Enrich(x, players)).ToList(),
            dropped.Select(x => Enrich(x, players)).ToList()));
    }

    private static TimeSpan? ParseWindow(string? window) => window switch
    {
        null or "" => TimeSpan.FromDays(7),
        "1d" => TimeSpan.FromDays(1),
        "7d" => TimeSpan.FromDays(7),
        "30d" => TimeSpan.FromDays(30),
        _ => null
    };

    private static IReadOnlyList<(string PlayerId, int Count)> TopByType(
        IReadOnlyList<TransferEntry> entries, TransferType type) =>
        entries
            .Where(e => e.Type == type)
            .GroupBy(e => e.PlayerId)
            .Select(g => (PlayerId: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.PlayerId, StringComparer.Ordinal)
            .Take(TopN)
            .ToList();

    private async Task<Dictionary<string, Player>> LoadPlayersAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var players = new Dictionary<string, Player>();
        foreach (var id in ids.Distinct())
        {
            var p = await _players.GetByIdAsync(id, ct);
            if (p is not null) players[id] = p;
        }
        return players;
    }

    private static TransferTrendEntry Enrich((string PlayerId, int Count) x, IReadOnlyDictionary<string, Player> players)
    {
        players.TryGetValue(x.PlayerId, out var p);
        return new TransferTrendEntry(x.PlayerId, p?.Name, p?.ClubName, x.Count);
    }
}
