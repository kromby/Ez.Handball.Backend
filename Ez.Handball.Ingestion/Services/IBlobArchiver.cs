namespace Ez.Handball.Ingestion.Services;

public interface IBlobArchiver
{
    Task SaveAsync(string path, string json, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task<string> ReadAsync(string path, CancellationToken ct = default);
}
