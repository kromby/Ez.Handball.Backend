using System.Globalization;
using System.Linq;
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure;

// Domain/game clock for the time-shift replay harness (#94). When the master override flag is
// off (production), returns the real wall-clock UTC with no table I/O. When on (debug, non-prod),
// reads a virtual `now` from the Config table on every call — never cached, so moving the date
// takes effect immediately. Auth time stays on the separate Func<DateTimeOffset> wall clock.
public sealed class GameClock : TimeProvider
{
    public const string OverrideGroup = "debug-clock-v1";
    public const string OverrideKey = "virtualNow";

    private readonly bool _overrideEnabled;
    private readonly TableClient _config;

    // configTableName defaults to the shared Config table; overridable for isolated tests.
    // Deliberately takes a concrete TableServiceClient and reads via the synchronous TableClient.Query
    // below: TimeProvider.GetUtcNow() is synchronous and cannot await the async ITableQuery the rest
    // of Infrastructure uses. The override read is gated to debug/non-prod, so the blocking call never
    // runs in production (flag off = no I/O).
    public GameClock(bool overrideEnabled, TableServiceClient tableServiceClient, string? configTableName = null)
    {
        _overrideEnabled = overrideEnabled;
        _config = tableServiceClient.GetTableClient(configTableName ?? Tables.Config);
    }

    public override DateTimeOffset GetUtcNow()
    {
        if (!_overrideEnabled) return TimeProvider.System.GetUtcNow();
        return TryReadVirtualNow(out var virtualNow) ? virtualNow : TimeProvider.System.GetUtcNow();
    }

    private bool TryReadVirtualNow(out DateTimeOffset virtualNow)
    {
        virtualNow = default;
        try
        {
            // OverrideGroup/OverrideKey are compile-time consts with no quote characters, so no ODataFilter.Escape
            // is needed here (unlike the user-derived group names elsewhere in this layer).
            var filter = $"PartitionKey eq '{OverrideGroup}' and RowKey eq '{OverrideKey}'";
            var row = _config.Query<ConfigEntity>(filter: filter, maxPerPage: 1).FirstOrDefault();
            if (row is null) return false;
            return DateTimeOffset.TryParse(
                row.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out virtualNow);
        }
        catch (RequestFailedException)
        {
            return false; // table missing / transient → wall-clock fallback
        }
    }
}
