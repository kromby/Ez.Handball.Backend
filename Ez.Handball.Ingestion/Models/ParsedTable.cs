namespace Ez.Handball.Ingestion.Models;

public sealed record ParsedTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public int RowCount => Rows.Count;
}
