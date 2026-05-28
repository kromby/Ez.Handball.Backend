using Azure;
using Azure.Data.Tables;
using System.Runtime.CompilerServices;

namespace Ez.Handball.Infrastructure.TableAccess;

public class TableQuery : ITableQuery
{
    private readonly TableServiceClient _client;

    public TableQuery(TableServiceClient client)
    {
        _client = client;
    }

    public async IAsyncEnumerable<T> QueryAsync<T>(
        string tableName,
        string filter,
        [EnumeratorCancellation] CancellationToken ct = default)
        where T : class, ITableEntity, new()
    {
        var table = _client.GetTableClient(tableName);
        var results = new List<T>();
        try
        {
            await foreach (var row in table.QueryAsync<T>(filter, cancellationToken: ct))
                results.Add(row);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        foreach (var item in results)
            yield return item;
    }
}
