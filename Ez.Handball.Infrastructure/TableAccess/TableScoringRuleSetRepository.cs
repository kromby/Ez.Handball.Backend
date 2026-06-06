using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableScoringRuleSetRepository : IScoringRuleSetRepository
{
    private readonly ITableQuery _query;

    public TableScoringRuleSetRepository(ITableQuery query) => _query = query;

    public async Task<ScoringRuleSet?> GetAsync(ValueFlavor flavor, int version, CancellationToken ct)
    {
        var group = $"{flavor.ToString().ToLowerInvariant()}-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGet(values, "goals", out var goals) ||
            !TryGet(values, "yellowCards", out var yellow) ||
            !TryGet(values, "twoMinute", out var twoMin) ||
            !TryGet(values, "redCards", out var red) ||
            !TryGet(values, "appearances", out var appearances))
            return null;

        return new ScoringRuleSet(flavor, version, goals, yellow, twoMin, red, appearances);
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> values, string key, out double result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
