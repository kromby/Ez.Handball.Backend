using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableSalaryRuleSetRepository : ISalaryRuleSetRepository
{
    private const string BandPrefix = "band:";

    private readonly ITableQuery _query;

    public TableSalaryRuleSetRepository(ITableQuery query) => _query = query;

    public async Task<SalaryRuleSet?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-price-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGetInt(values, "minGames", out var minGames) ||
            !values.TryGetValue("currency", out var currency))
            return null;

        var bands = new List<SalaryBand>();
        foreach (var kv in values)
        {
            if (!kv.Key.StartsWith(BandPrefix, StringComparison.Ordinal)) continue;
            if (double.TryParse(kv.Key[BandPrefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
                && double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var price))
                bands.Add(new SalaryBand(threshold, price));
        }

        if (bands.Count == 0) return null;
        bands.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));

        return new SalaryRuleSet(version, minGames, currency, bands);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
