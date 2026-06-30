using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class StatsTableParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Parse_Outfield_DecodesHeadersAndReturnsRows()
    {
        var table = StatsTableParser.Parse(Fixture("outfield.html"));

        Assert.Contains("Nafn", table.Columns);
        Assert.Contains("Mörk", table.Columns);   // HTML entity M&ouml;rk decoded
        Assert.Contains("xG", table.Columns);      // advanced metric HSÍ lacks
        Assert.True(table.RowCount > 0);
        Assert.All(table.Rows, r => Assert.Equal(table.Columns.Count, r.Count));
    }

    [Fact]
    public void Parse_Gk_CapturesClubAndExpectedSaves()
    {
        var table = StatsTableParser.Parse(Fixture("gk-inner.html"));

        Assert.Contains("Nafn", table.Columns);
        Assert.Contains("xS", table.Columns);      // expected saves
        Assert.True(table.RowCount > 0);
        // club is captured either as the abbrev text cell or from the logo image filename
        var firstRowText = string.Join("|", table.Rows[0]).ToLowerInvariant();
        Assert.Matches("ka|val|fh|haukar|afturelding|stjarnan|fram|selfoss|grotta|ir|hk", firstRowText);
    }
}
