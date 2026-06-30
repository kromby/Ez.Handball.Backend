using System.Text.Json;
using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class TableSerializerTests
{
    private static ParsedTable Sample() => new(
        Columns: new[] { "Nafn", "Lið", "Mörk" },
        Rows: new IReadOnlyList<string>[]
        {
            new[] { "Jón, Jónsson", "KA", "5.5" },
            new[] { "Quote\"Man", "Valur", "3.0" },
        });

    [Fact]
    public void ToCsv_QuotesFieldsWithCommasAndQuotes()
    {
        var csv = TableSerializer.ToCsv(Sample());
        var lines = csv.Replace("\r\n", "\n").Trim().Split('\n');

        Assert.Equal("Nafn,Lið,Mörk", lines[0]);
        Assert.Equal("\"Jón, Jónsson\",KA,5.5", lines[1]);
        Assert.Equal("\"Quote\"\"Man\",Valur,3.0", lines[2]);
    }

    [Fact]
    public void ToJson_ProducesObjectPerRowKeyedByColumn()
    {
        var json = TableSerializer.ToJson(Sample());
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("KA", doc.RootElement[0].GetProperty("Lið").GetString());
        Assert.Equal("5.5", doc.RootElement[0].GetProperty("Mörk").GetString());
    }
}
