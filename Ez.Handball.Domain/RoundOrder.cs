namespace Ez.Handball.Domain;

// Orders HSÍ round labels: numeric rounds ascending first, non-numeric rounds last.
// Shared by the gameweek score read and the manager-standings ranker so both agree
// on what "the latest round" and "the previous round" mean.
public static class RoundOrder
{
    public static (int, int) Key(string round)
        => int.TryParse(round, out var n) ? (0, n) : (1, 0);

    public static int Compare(string a, string b)
    {
        var c = Key(a).CompareTo(Key(b));
        return c != 0 ? c : string.CompareOrdinal(a, b);
    }
}
