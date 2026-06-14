using System.Globalization;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableTransferLedgerRepository : ITransferLedgerRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableTransferLedgerRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task AppendAsync(TransferEntry entry, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTransferLedger);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var entity = new TransferEntryEntity
        {
            PartitionKey = BucketKey(entry.Flavor, entry.CreatedAt),
            RowKey = RowKey(entry),
            UserId = entry.UserId,
            PlayerId = entry.PlayerId,
            Flavor = FlavorString(entry.Flavor),
            Type = TypeString(entry.Type),
            Cost = entry.Cost,
            SeasonLabel = entry.SeasonLabel,
            CreatedAt = entry.CreatedAt
        };
        await table.AddEntityAsync(entity, ct);
    }

    public async Task<IReadOnlyList<TransferEntry>> ListSinceAsync(
        GameFlavor flavor, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var buckets = BucketsBetween(flavor, from, to);
        var entries = new List<TransferEntry>();
        foreach (var bucket in buckets)
        {
            await foreach (var e in _query.QueryAsync<TransferEntryEntity>(
                               Tables.GameTransferLedger, $"PartitionKey eq '{ODataFilter.Escape(bucket)}'", ct))
            {
                if (e.CreatedAt >= from && e.CreatedAt <= to)
                    entries.Add(ToEntry(e));
            }
        }
        return entries;
    }

    private static HashSet<string> BucketsBetween(GameFlavor flavor, DateTimeOffset from, DateTimeOffset to)
    {
        var keys = new HashSet<string> { BucketKey(flavor, from), BucketKey(flavor, to) };
        for (var d = from.UtcDateTime.Date; d <= to.UtcDateTime.Date; d = d.AddDays(1))
            keys.Add(BucketKey(flavor, new DateTimeOffset(d, TimeSpan.Zero)));
        return keys;
    }

    private static string BucketKey(GameFlavor flavor, DateTimeOffset at)
    {
        var dt = at.UtcDateTime;
        return $"{FlavorString(flavor)}-{ISOWeek.GetYear(dt):D4}-W{ISOWeek.GetWeekOfYear(dt):D2}";
    }

    private static string RowKey(TransferEntry entry)
    {
        var reverse = (DateTimeOffset.MaxValue.Ticks - entry.CreatedAt.UtcDateTime.Ticks).ToString("D19", CultureInfo.InvariantCulture);
        return $"{reverse}-{entry.PlayerId}-{entry.UserId}";
    }

    private static string FlavorString(GameFlavor f) => f.ToString().ToLowerInvariant();
    private static string TypeString(TransferType t) => t.ToString().ToLowerInvariant();

    private static TransferEntry ToEntry(TransferEntryEntity e) => new(
        e.UserId, e.PlayerId,
        Enum.Parse<GameFlavor>(e.Flavor, ignoreCase: true),
        Enum.Parse<TransferType>(e.Type, ignoreCase: true),
        e.Cost, e.SeasonLabel, e.CreatedAt);
}
