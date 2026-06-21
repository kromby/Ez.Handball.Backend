using AngleSharp.Html.Parser;

namespace HbStatz.Spike;

public static class IframeResolver
{
    public static bool HasInlineStatsTable(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        return doc.QuerySelector("table#statz1") != null;
    }

    public static IReadOnlyList<string> ExtractSources(string html, string baseUrl)
    {
        var baseUri = new Uri(baseUrl);
        var doc = new HtmlParser().ParseDocument(html);
        return doc.QuerySelectorAll("iframe")
            .Select(f => f.GetAttribute("src"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new Uri(baseUri, s!).AbsoluteUri)
            .Distinct()
            .ToList();
    }
}
