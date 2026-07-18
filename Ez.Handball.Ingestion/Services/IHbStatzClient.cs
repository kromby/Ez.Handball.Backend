namespace Ez.Handball.Ingestion.Services;

public interface IHbStatzClient
{
    Task<string> GetHtmlAsync(string url, CancellationToken ct = default);
}
