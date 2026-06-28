namespace HbStatz.Spike;

public sealed class HbStatzClient(HttpClient http)
{
    private const string UserAgent =
        "EzHandball-spike/0.1 (+https://github.com/kromby/Ez.Handball.Backend/issues/7)";

    public async Task<IReadOnlyList<string>> GetTableHtmlsAsync(string pageUrl, CancellationToken ct = default)
    {
        var html = await GetHtmlAsync(pageUrl, ct);
        if (IframeResolver.HasInlineStatsTable(html))
            return new[] { html };

        var results = new List<string>();
        foreach (var src in IframeResolver.ExtractSources(html, pageUrl))
            results.Add(await GetHtmlAsync(src, ct));
        return results;
    }

    public async Task<string> GetHtmlAsync(string url, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
