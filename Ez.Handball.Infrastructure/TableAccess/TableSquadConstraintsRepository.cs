using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableSquadConstraintsRepository : ISquadConstraintsRepository
{
    private const string PosLimitPrefix = "posLimit:";

    private readonly ITableQuery _query;

    public TableSquadConstraintsRepository(ITableQuery query) => _query = query;

    public async Task<SquadConstraints?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-squad-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGetDouble(values, "startingCap", out var startingCap) ||
            !TryGetInt(values, "maxSquadSize", out var maxSquadSize) ||
            !values.TryGetValue("currency", out var currency))
            return null;

        var limits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            if (!kv.Key.StartsWith(PosLimitPrefix, StringComparison.Ordinal)) continue;
            var position = kv.Key[PosLimitPrefix.Length..];
            if (int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit))
                limits[position] = limit;
        }

        return new SquadConstraints(version, maxSquadSize, limits, startingCap, currency);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, string> values, string key, out double result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
