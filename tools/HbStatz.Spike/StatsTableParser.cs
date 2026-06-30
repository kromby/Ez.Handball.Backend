using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace HbStatz.Spike;

public static class StatsTableParser
{
    public static ParsedTable Parse(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);

        var table = doc.QuerySelector("table#statz1")
                    ?? doc.QuerySelectorAll("table").FirstOrDefault(t => t.QuerySelector("thead") != null)
                    ?? throw new InvalidOperationException("No stats table found in HTML.");

        var columns = table.QuerySelectorAll("thead th, thead td")
            .Select(CellText)
            .ToList();

        var rows = table.QuerySelectorAll("tbody tr")
            .Select(tr => (IReadOnlyList<string>)tr.QuerySelectorAll("td").Select(CellText).ToList())
            .Where(r => r.Count > 0)
            .ToList();

        return new ParsedTable(columns, rows);
    }

    public static IReadOnlyList<ParsedTable> ParseAll(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);

        return doc.QuerySelectorAll("table")
            .Where(t => t.QuerySelector("thead") != null)
            .Select(table =>
            {
                var columns = table.QuerySelectorAll("thead th, thead td")
                    .Select(CellText)
                    .ToList();

                var rows = table.QuerySelectorAll("tbody tr")
                    .Select(tr => (IReadOnlyList<string>)tr.QuerySelectorAll("td").Select(CellText).ToList())
                    .Where(r => r.Count > 0)
                    .ToList();

                return new ParsedTable(columns, rows);
            })
            .ToList();
    }

    private static string CellText(IElement cell)
    {
        var text = cell.TextContent.Trim();
        if (!string.IsNullOrEmpty(text)) return text;

        var img = cell.QuerySelector("img");
        var src = img?.GetAttribute("src");
        if (string.IsNullOrEmpty(src)) return string.Empty;

        var file = src.Split('/').Last();           // player_images/ka.png -> ka.png
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;          // ka.png -> ka
    }
}
