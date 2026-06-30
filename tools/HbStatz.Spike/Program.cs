namespace HbStatz.Spike;

public static class Program
{
    private record Target(string Name, string Url);

    private static readonly Target[] Targets =
    {
        new("outfield",    "https://hbstatz.is/OlisDeildKarlaTolfraedi.php"),
        new("goalkeepers", "https://hbstatz.is/OlisDeildKarlaTolfraedi2.php"),
    };

    private static readonly string[] DefaultGames = { "12922", "12919" };

    public static async Task<int> Main(string[] args)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");
        Directory.CreateDirectory(outputDir);

        using var http = new HttpClient();

        if (args.Length > 0 && args[0] == "score")
        {
            var games = args.Length > 1 ? args[1..] : DefaultGames;
            await ScoreGamesAsync(new MatchReportClient(new HbStatzClient(http)), games, outputDir);
            return 0;
        }

        await ScrapeSeasonAsync(new HbStatzClient(http), outputDir);
        return 0;
    }

    private static async Task ScoreGamesAsync(MatchReportClient client, string[] games, string outputDir)
    {
        foreach (var matchId in games)
        {
            var lines = new List<PlayerStatLine>();
            foreach (var side in new[] { "home", "away" })
            {
                var html = await client.GetTeamPageHtmlAsync(matchId, side);
                lines.AddRange(MatchStatLineBuilder.Build(StatsTableParser.ParseAll(html), side));
            }

            var scored = lines
                .Select(p => (Player: p, Points: FantasyScorer.Score(p)))
                .OrderByDescending(x => x.Points)
                .ToList();

            Console.WriteLine($"=== Game {matchId} — {scored.Count} players ===");
            Console.WriteLine($"{"Player",-28} {"Side",-4} {"G",2} {"Y",2} {"2m",2} {"R",2} {"Pts",5}");
            foreach (var (p, pts) in scored)
                Console.WriteLine($"{p.Name,-28} {p.Side,-4} {p.Goals,2} {p.YellowCards,2} {p.TwoMinuteSuspensions,2} {p.RedCards,2} {pts,5}");

            var table = new ParsedTable(
                new[] { "side", "jersey", "name", "goalkeeper", "goals", "yellow", "twoMin", "red", "points" },
                scored.Select(x => (IReadOnlyList<string>)new[]
                {
                    x.Player.Side,
                    x.Player.Jersey?.ToString() ?? "",
                    x.Player.Name,
                    x.Player.IsGoalkeeper ? "true" : "false",
                    x.Player.Goals.ToString(),
                    x.Player.YellowCards.ToString(),
                    x.Player.TwoMinuteSuspensions.ToString(),
                    x.Player.RedCards.ToString(),
                    x.Points.ToString(),
                }).ToList());

            await File.WriteAllTextAsync(Path.Combine(outputDir, $"scored-{matchId}.csv"), TableSerializer.ToCsv(table));
        }

        Console.WriteLine($"Scored CSVs written to {Path.GetFullPath(outputDir)}");
    }

    private static async Task ScrapeSeasonAsync(HbStatzClient client, string outputDir)
    {
        foreach (var target in Targets)
        {
            var htmls = await client.GetTableHtmlsAsync(target.Url);
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
    }
}
