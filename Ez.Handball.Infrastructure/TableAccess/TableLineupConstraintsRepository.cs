using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableLineupConstraintsRepository : ILineupConstraintsRepository
{
    private const string MinPrefix = "startMin:";
    private const string MaxPrefix = "startMax:";

    private readonly ITableQuery _query;

    public TableLineupConstraintsRepository(ITableQuery query) => _query = query;

    public async Task<LineupConstraints?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-lineup-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGetInt(values, "starterCount", out var starterCount) ||
            !TryGetDouble(values, "captainMultiplier", out var captainMultiplier))
            return null;

        var captainRequired = !values.TryGetValue("captainRequired", out var cr) || !bool.TryParse(cr, out var crv) || crv;
        var viceRequired = values.TryGetValue("viceRequired", out var vr) && bool.TryParse(vr, out var vrv) && vrv;

        var mins = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            if (kv.Key.StartsWith(MinPrefix, StringComparison.Ordinal)
                && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
                mins[kv.Key[MinPrefix.Length..]] = min;
            else if (kv.Key.StartsWith(MaxPrefix, StringComparison.Ordinal)
                && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
                maxs[kv.Key[MaxPrefix.Length..]] = max;
        }

        var positionStart = new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal);
        foreach (var position in mins.Keys.Union(maxs.Keys))
        {
            mins.TryGetValue(position, out var min);
            // A position with only a min defaults max to the full starter count (effectively no cap).
            var max = maxs.TryGetValue(position, out var m) ? m : starterCount;
            positionStart[position] = (min, max);
        }

        return new LineupConstraints(version, starterCount, positionStart, captainMultiplier, captainRequired, viceRequired);
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
