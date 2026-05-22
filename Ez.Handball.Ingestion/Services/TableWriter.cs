using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Ingestion.Services;

public class TableWriter : ITableWriter
{
    private readonly TableServiceClient _serviceClient;

    public TableWriter(TableServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public async Task UpsertAsync<T>(string tableName, T entity, CancellationToken ct = default)
        where T : ITableEntity
    {
        var table = _serviceClient.GetTableClient(tableName);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task<T?> GetAsync<T>(string tableName, string partitionKey, string rowKey, CancellationToken ct = default)
        where T : class, ITableEntity, new()
    {
        var table = _serviceClient.GetTableClient(tableName);
        try
        {
            var response = await table.GetEntityAsync<T>(partitionKey, rowKey, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IList<T>> QueryAsync<T>(string tableName, string filter, CancellationToken ct = default)
        where T : class, ITableEntity, new()
    {
        var table = _serviceClient.GetTableClient(tableName);
        var results = new List<T>();
        try
        {
            await foreach (var entity in table.QueryAsync<T>(filter, cancellationToken: ct))
            {
                results.Add(entity);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Table doesn't exist yet — treat as an empty result. Callers handle "empty" already
            // (e.g., ParseMatch/ParsePlayers throw and let the blob trigger retry, by which point
            // the table has usually been created by a sibling parse function).
        }
        return results;
    }
}
