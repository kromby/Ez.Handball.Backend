using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HbStatz.Spike;

public static class TableSerializer
{
    public static string ToCsv(ParsedTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Columns.Select(Escape)));
        foreach (var row in table.Rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        return sb.ToString();
    }

    public static string ToJson(ParsedTable table)
    {
        var keys = UniqueKeys(table.Columns);
        var objects = table.Rows.Select(row =>
        {
            var obj = new Dictionary<string, string>();
            for (var i = 0; i < keys.Count && i < row.Count; i++)
                obj[keys[i]] = row[i];
            return obj;
        });
        return JsonSerializer.Serialize(objects, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Icelandic chars readable
        });
    }

    private static string Escape(string field)
    {
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }

    private static List<string> UniqueKeys(IReadOnlyList<string> columns)
    {
        var seen = new Dictionary<string, int>();
        var keys = new List<string>();
        foreach (var c in columns)
        {
            var key = string.IsNullOrEmpty(c) ? "col" : c;
            if (seen.TryGetValue(key, out var n))
            {
                seen[key] = n + 1;
                key = $"{key}_{n + 1}";
            }
            else seen[key] = 1;
            keys.Add(key);
        }
        return keys;
    }
}
