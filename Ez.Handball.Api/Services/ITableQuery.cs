using Azure.Data.Tables;

namespace Ez.Handball.Api.Services;

public interface ITableQuery
{
    IAsyncEnumerable<T> QueryAsync<T>(string tableName, string filter, CancellationToken ct = default)
        where T : class, ITableEntity, new();
}
