namespace Ez.Handball.Domain;

// Deterministic team colour derived from the favourite club id. Pure function — the same
// club always yields the same colour. Stored on the GameTeam at registration (see spec).
public static class ManagerColor
{
    // Curated, readable palette. Order is stable; do not reorder (it would change existing
    // teams' derived colour if ever recomputed).
    private static readonly string[] Palette =
    {
        "#1E88E5", "#43A047", "#E53935", "#FB8C00", "#8E24AA", "#00897B",
        "#3949AB", "#F4511E", "#7CB342", "#C0CA33", "#00ACC1", "#5E35B1",
        "#D81B60", "#6D4C41", "#039BE5", "#FDD835",
    };

    public static string ForClub(string? clubId)
    {
        if (string.IsNullOrEmpty(clubId)) return Palette[0];

        // FNV-1a over the UTF-16 chars of the id (club ids are ASCII) — stable across
        // processes (unlike string.GetHashCode).
        uint hash = 2166136261;
        foreach (var c in clubId)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return Palette[(int)(hash % (uint)Palette.Length)];
    }
}
