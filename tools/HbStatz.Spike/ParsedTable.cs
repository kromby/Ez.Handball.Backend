namespace HbStatz.Spike;

public sealed record ParsedTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public int RowCount => Rows.Count;
}
