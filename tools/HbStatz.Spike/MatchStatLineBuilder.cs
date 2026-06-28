namespace HbStatz.Spike;

public static class MatchStatLineBuilder
{
    public static IReadOnlyList<PlayerStatLine> Build(IReadOnlyList<ParsedTable> teamTables, string side)
    {
        var gkTable = teamTables.FirstOrDefault(t =>
            t.Columns.Contains("Nafn") && t.Columns.Contains("Varin"));
        var offensiveTable = teamTables.FirstOrDefault(t =>
            t.Columns.Contains("Nafn") && t.Columns.Contains("Mörk")
            && !t.Columns.Contains("Varin") && !t.Columns.Contains("Gul"));
        var disciplineTable = teamTables.FirstOrDefault(t =>
            t.Columns.Contains("Nafn") && t.Columns.Contains("Gul")
            && !t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin"));

        var lines = new List<PlayerStatLine>();

        // Goalkeepers: all inputs in one row.
        if (gkTable is not null)
        {
            foreach (var row in gkTable.Rows)
            {
                var (jersey, name) = SplitPlayer(Cell(gkTable, row, "Nafn"));
                lines.Add(new PlayerStatLine(side, jersey, name, IsGoalkeeper: true,
                    Goals: Int(Cell(gkTable, row, "Mörk")),
                    YellowCards: Int(Cell(gkTable, row, "Gul")),
                    TwoMinuteSuspensions: Int(Cell(gkTable, row, "2Mín")),
                    RedCards: Int(Cell(gkTable, row, "Rau"))));
            }
        }

        // Outfield: goals from offensive, cards from discipline, joined by player cell.
        var cardsByPlayer = new Dictionary<string, IReadOnlyList<string>>();
        if (disciplineTable is not null)
            foreach (var row in disciplineTable.Rows)
                cardsByPlayer[Cell(disciplineTable, row, "Nafn")] = row;

        if (offensiveTable is not null)
        {
            foreach (var row in offensiveTable.Rows)
            {
                var key = Cell(offensiveTable, row, "Nafn");
                var (jersey, name) = SplitPlayer(key);
                var hasCards = cardsByPlayer.TryGetValue(key, out var cardRow);
                lines.Add(new PlayerStatLine(side, jersey, name, IsGoalkeeper: false,
                    Goals: Int(Cell(offensiveTable, row, "Mörk")),
                    YellowCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Gul")) : 0,
                    TwoMinuteSuspensions: hasCards ? Int(Cell(disciplineTable!, cardRow!, "2Mín")) : 0,
                    RedCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Rau")) : 0));
            }
        }

        return lines;
    }

    private static string Cell(ParsedTable table, IReadOnlyList<string> row, string column)
    {
        var idx = table.Columns.ToList().IndexOf(column);
        return idx >= 0 && idx < row.Count ? row[idx] : string.Empty;
    }

    private static int Int(string value) => int.TryParse(value.Trim(), out var n) ? n : 0;

    private static (int? Jersey, string Name) SplitPlayer(string cell)
    {
        var dot = cell.IndexOf('.');
        if (dot > 0 && int.TryParse(cell[..dot].Trim(), out var jersey))
            return (jersey, cell[(dot + 1)..].Trim());
        return (null, cell.Trim());
    }
}
