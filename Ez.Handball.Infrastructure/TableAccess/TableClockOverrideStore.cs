using System.Globalization;
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

// Writes the debug-clock override row GameClock reads. configTableName defaults to the shared
// Config table; overridable for isolated tests (mirrors GameClock's ctor).
internal sealed class TableClockOverrideStore : IClockOverrideStore
{
    private readonly TableClient _config;

    public TableClockOverrideStore(TableServiceClient client, string? configTableName = null)
        => _config = client.GetTableClient(configTableName ?? Tables.Config);

    public async Task SetAsync(DateTimeOffset utc, CancellationToken ct)
    {
        await _config.CreateIfNotExistsAsync(cancellationToken: ct);
        // "yyyy-MM-ddTHH:mm:ssZ" round-trips through GameClock's DateTimeOffset.TryParse
        // (AssumeUniversal | AdjustToUniversal). Advance targets are whole-second instants.
        var value = utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        await _config.UpsertEntityAsync(new ConfigEntity
        {
            PartitionKey = GameClock.OverrideGroup,
            RowKey = GameClock.OverrideKey,
            Value = value
        }, TableUpdateMode.Replace, ct);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        try
        {
            await _config.DeleteEntityAsync(GameClock.OverrideGroup, GameClock.OverrideKey, cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // already absent → nothing to clear
        }
    }
}
