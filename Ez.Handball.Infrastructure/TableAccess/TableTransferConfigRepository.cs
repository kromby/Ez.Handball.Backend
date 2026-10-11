using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableTransferConfigRepository : ITransferConfigRepository
{
    private readonly ITableQuery _query;

    public TableTransferConfigRepository(ITableQuery query) => _query = query;

    public async Task<TransferConfig?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-transfers-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        return new TransferConfig(
            version,
            GetInt(values, "freeTransfersPerRound", 1),
            GetInt(values, "maxBankedFreeTransfers", 1),
            GetInt(values, "pointsPerExtraTransfer", 4),
            GetInt(values, "startsFromRound", 1));
    }

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
