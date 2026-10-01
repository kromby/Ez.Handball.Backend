using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableMatchFinalOverrideRepository : IMatchFinalOverrideRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableMatchFinalOverrideRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<IReadOnlySet<string>> ListMatchIdsAsync(string tournamentId, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var filter = $"PartitionKey eq '{ODataFilter.Escape(tournamentId)}'";
        await foreach (var e in _query.QueryAsync<MatchFinalOverrideEntity>(Tables.MatchFinalOverrides, filter, ct))
            ids.Add(e.RowKey);
        return ids;
    }

    public async Task SetAsync(string tournamentId, string matchId, string setBy, DateTimeOffset setAt, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MatchFinalOverrides);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new MatchFinalOverrideEntity
        {
            PartitionKey = tournamentId,
            RowKey = matchId,
            SetBy = setBy,
            SetAt = setAt
        }, TableUpdateMode.Replace, ct);
    }

    public async Task ClearAsync(string tournamentId, string matchId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MatchFinalOverrides);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        try
        {
            await table.DeleteEntityAsync(tournamentId, matchId, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Nothing to clear.
        }
    }
}
