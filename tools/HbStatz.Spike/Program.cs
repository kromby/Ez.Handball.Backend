namespace HbStatz.Spike;

public static class Program
{
    private record Target(string Name, string Url);

    private static readonly Target[] Targets =
    {
        new("outfield",    "https://hbstatz.is/OlisDeildKarlaTolfraedi.php"),
        new("goalkeepers", "https://hbstatz.is/OlisDeildKarlaTolfraedi2.php"),
    };

    public static async Task<int> Main(string[] args)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");
        Directory.CreateDirectory(outputDir);

        using var http = new HttpClient();
        var client = new HbStatzClient(http);

        foreach (var target in Targets)
        {
            var htmls = await client.GetTableHtmlsAsync(target.Url);
            // a target may resolve to multiple iframe tables; take the first non-empty stats table
            ParsedTable? table = null;
            foreach (var html in htmls)
            {
                try { var t = StatsTableParser.Parse(html); if (t.RowCount > 0) { table = t; break; } }
                catch (InvalidOperationException) { /* not the table-bearing fragment */ }
            }

            if (table is null)
            {
                Console.WriteLine($"[{target.Name}] NO TABLE FOUND at {target.Url}");
                continue;
            }

            await File.WriteAllTextAsync(Path.Combine(outputDir, $"{target.Name}.json"), TableSerializer.ToJson(table));
            await File.WriteAllTextAsync(Path.Combine(outputDir, $"{target.Name}.csv"),  TableSerializer.ToCsv(table));

            Console.WriteLine($"[{target.Name}] {table.RowCount} rows, {table.Columns.Count} columns");
            Console.WriteLine($"  columns: {string.Join(", ", table.Columns)}");
            Console.WriteLine($"  row[0] : {string.Join(" | ", table.Rows[0])}");
        }

        Console.WriteLine($"Output written to {Path.GetFullPath(outputDir)}");
        return 0;
    }
}
