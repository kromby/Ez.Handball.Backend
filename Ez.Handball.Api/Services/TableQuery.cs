using Azure.Data.Tables;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ez.Handball.Api.Services;

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
        await foreach (var row in table.QueryAsync<T>(filter, cancellationToken: ct))
        {
            yield return row;
        }
    }
}
