namespace Ez.Handball.Ingestion.Services;

public sealed class HbStatzClient : IHbStatzClient
{
    private readonly HttpClient _http;

    public HbStatzClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetHtmlAsync(string url, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
