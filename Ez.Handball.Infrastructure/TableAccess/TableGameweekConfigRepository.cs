using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekConfigRepository : IGameweekConfigRepository
{
    private readonly ITableQuery _query;

    public TableGameweekConfigRepository(ITableQuery query) => _query = query;

    public async Task<GameweekConfig?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-gameweek-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;
        if (!values.TryGetValue("tournamentId", out var tournamentId) || string.IsNullOrWhiteSpace(tournamentId))
            return null;

        var lockOffsetHours = GetDouble(values, "lockOffsetHours", 1);
        var scoringVersion = GetInt(values, "scoringRuleSetVersion", 1);
        var lineupVersion = GetInt(values, "lineupConstraintsVersion", 1);

        return new GameweekConfig(version, tournamentId, lockOffsetHours, scoringVersion, lineupVersion);
    }

    private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback)
        => values.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
