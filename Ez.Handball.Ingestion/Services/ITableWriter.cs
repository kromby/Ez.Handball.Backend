using Azure.Data.Tables;

namespace Ez.Handball.Ingestion.Services;

public interface ITableWriter
{
    Task UpsertAsync<T>(string tableName, T entity, CancellationToken ct = default,
        TableUpdateMode mode = TableUpdateMode.Replace)
        where T : ITableEntity;

    Task<T?> GetAsync<T>(string tableName, string partitionKey, string rowKey, CancellationToken ct = default)
        where T : class, ITableEntity, new();

    Task<IList<T>> QueryAsync<T>(string tableName, string filter, CancellationToken ct = default)
        where T : class, ITableEntity, new();
}
