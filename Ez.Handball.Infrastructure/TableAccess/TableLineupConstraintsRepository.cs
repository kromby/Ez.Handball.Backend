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

        var captainRequired = GetBool(values, "captainRequired", defaultValue: true);
        var viceRequired = GetBool(values, "viceRequired", defaultValue: false);
        var positionStart = ParsePositionStart(values, starterCount);

        return new LineupConstraints(version, starterCount, positionStart, captainMultiplier, captainRequired, viceRequired);
    }

    // Build the per-position starter min/max map from startMin:/startMax: rows. A position with
    // only a min defaults its max to the full starter count (effectively no upper cap).
    private static IReadOnlyDictionary<string, (int Min, int Max)> ParsePositionStart(
        IReadOnlyDictionary<string, string> values, int starterCount)
    {
        var mins = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            if (TryParsePrefixedInt(kv, MinPrefix, out var minPos, out var min)) mins[minPos] = min;
            else if (TryParsePrefixedInt(kv, MaxPrefix, out var maxPos, out var max)) maxs[maxPos] = max;
        }

        var positionStart = new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal);
        foreach (var position in mins.Keys.Union(maxs.Keys))
        {
            mins.TryGetValue(position, out var min);
            var max = maxs.TryGetValue(position, out var m) ? m : starterCount;
            positionStart[position] = (min, max);
        }
        return positionStart;
    }

    private static bool TryParsePrefixedInt(
        KeyValuePair<string, string> row, string prefix, out string position, out int value)
    {
        position = string.Empty;
        value = 0;
        if (!row.Key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return false;
        position = row.Key[prefix.Length..];
        return true;
    }

    // Defaults when the key is absent or unparseable.
    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        => values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed) ? parsed : defaultValue;

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
