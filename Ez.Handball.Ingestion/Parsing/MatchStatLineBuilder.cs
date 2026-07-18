using Ez.Handball.Ingestion.Models;

namespace Ez.Handball.Ingestion.Parsing;

public static class MatchStatLineBuilder
{
    public static IReadOnlyList<PlayerStatLine> Build(IReadOnlyList<ParsedTable> teamTables, string side)
    {
        var gkTable = teamTables.FirstOrDefault(t => t.Columns.Contains("Nafn") && t.Columns.Contains("Varin"));
        var offensiveTable = teamTables.FirstOrDefault(t => t.Columns.Contains("Nafn") && t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin") && !t.Columns.Contains("Gul"));
        var disciplineTable = teamTables.FirstOrDefault(t => t.Columns.Contains("Nafn") && t.Columns.Contains("Gul") && !t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin"));

        var lines = new List<PlayerStatLine>();

        if (gkTable is not null)
        {
            foreach (var row in gkTable.Rows)
            {
                var (jersey, name) = SplitPlayer(Cell(gkTable, row, "Nafn"));
                lines.Add(new PlayerStatLine(
                    Side: side, Jersey: jersey, Name: name, IsGoalkeeper: true,
                    Goals: Int(Cell(gkTable, row, "Mörk")),
                    YellowCards: Int(Cell(gkTable, row, "Gul")),
                    TwoMinuteSuspensions: Int(Cell(gkTable, row, "2Mín")),
                    RedCards: Int(Cell(gkTable, row, "Rau")),
                    Assists: NullInt(Cell(gkTable, row, "Sköpuð færi (Stoð)")),
                    Turnovers: NullInt(Cell(gkTable, row, "TB (TB/ S.Brot)")),
                    Steals: NullInt(Cell(gkTable, row, "Stl")),
                    PlusMinus: Double(Cell(gkTable, row, "+/-")),
                    Saves: NullInt(Cell(gkTable, row, "Varin")),
                    SaveRate: Double(Cell(gkTable, row, "%")),
                    ExpectedSaves: Double(Cell(gkTable, row, "xS")),
                    GoalsAgainst: Double(Cell(gkTable, row, "Mörk á")),
                    PenaltySaves: NullInt(Cell(gkTable, row, "Víti Varin"))
                ));
            }
        }

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

                lines.Add(new PlayerStatLine(
                    Side: side, Jersey: jersey, Name: name, IsGoalkeeper: false,
                    Goals: Int(Cell(offensiveTable, row, "Mörk")),
                    YellowCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Gul")) : 0,
                    TwoMinuteSuspensions: hasCards ? Int(Cell(disciplineTable!, cardRow!, "2Mín")) : 0,
                    RedCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Rau")) : 0,
                    Assists: NullInt(Cell(offensiveTable, row, "Sköpuð færi (Stoð)")),
                    Turnovers: NullInt(Cell(offensiveTable, row, "TB (TB/S.Brot)")),
                    Steals: hasCards ? NullInt(Cell(disciplineTable!, cardRow!, "Stolinn")) : null,
                    PlusMinus: Double(Cell(offensiveTable, row, "+/-")),
                    Shots: NullInt(Cell(offensiveTable, row, "Skot")),
                    Blocks: hasCards ? NullInt(Cell(disciplineTable!, cardRow!, "Blokk")) : null,
                    Stops: hasCards ? Double(Cell(disciplineTable!, cardRow!, "Lögleg Stopp")) : null,
                    ExpectedGoals: Double(Cell(offensiveTable, row, "xG")),
                    PenaltiesEarned: NullInt(Cell(offensiveTable, row, "Fi.V.")),
                    PenaltyGoals: NullInt(Cell(offensiveTable, row, "Víta Mörk"))
                ));
            }
        }

        return lines;
    }

    private static string Cell(ParsedTable table, IReadOnlyList<string> row, string column)
    {
        var idx = table.Columns.ToList().IndexOf(column);
        return idx >= 0 && idx < row.Count ? row[idx] : string.Empty;
    }

    private static string CleanNumericString(string val)
    {
        val = val.Trim();
        if (string.IsNullOrEmpty(val)) return string.Empty;

        var parts = val.Split(new[] { ' ', '(', '/', ')' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static int Int(string val)
    {
        var cleaned = CleanNumericString(val);
        return int.TryParse(cleaned, out var n) ? n : 0;
    }

    private static int? NullInt(string val)
    {
        var cleaned = CleanNumericString(val);
        return int.TryParse(cleaned, out var n) ? n : null;
    }

    private static double? Double(string val)
    {
        var cleaned = CleanNumericString(val).Replace("%", "").Trim();
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static (int? Jersey, string Name) SplitPlayer(string cell)
    {
        var dot = cell.IndexOf('.');
        if (dot > 0 && int.TryParse(cell[..dot].Trim(), out var jersey))
            return (jersey, cell[(dot + 1)..].Trim());
        return (null, cell.Trim());
    }
}
