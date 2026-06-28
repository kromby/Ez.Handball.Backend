using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class ParseAllTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pergame", name));

    [Fact]
    public void ParseAll_TeamPage_ReturnsAllTheadTables()
    {
        var tables = StatsTableParser.ParseAll(Fixture("12922-home.html"));

        // 5 tables on a team page; the three data tables are non-empty
        Assert.Equal(5, tables.Count);

        // GK table: has Nafn + Varin
        Assert.Contains(tables, t => t.Columns.Contains("Nafn") && t.Columns.Contains("Varin"));
        // Outfield offensive: Nafn + Mörk + Skot, no Varin, no Gul
        Assert.Contains(tables, t => t.Columns.Contains("Nafn") && t.Columns.Contains("Mörk")
            && t.Columns.Contains("Skot") && !t.Columns.Contains("Varin") && !t.Columns.Contains("Gul"));
        // Outfield discipline: Nafn + Gul, no Mörk, no Varin
        Assert.Contains(tables, t => t.Columns.Contains("Nafn") && t.Columns.Contains("Gul")
            && !t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin"));
    }
}
