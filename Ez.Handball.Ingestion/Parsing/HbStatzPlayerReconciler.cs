using Ez.Handball.Ingestion.Models;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Ingestion.Parsing;

// Reconciles an HBStatz player line (their own numeric player_id, name, jersey number) against
// our roster for the same team. HBStatz's player_id has no relationship to our playerId, so this
// always runs by name/jersey — but only once per game per player, against an already-loaded
// roster, not the per-game HTML-table-joining the old scraping spike needed.
public static class HbStatzPlayerReconciler
{
    public static string? Resolve(IList<PlayerEntity> roster, HbStatzPlayerLine line)
    {
        var normalizedName = Normalize(line.Name);
        var jersey = line.Number?.ToString();

        var byJerseyAndName = roster.FirstOrDefault(p =>
            jersey is not null && p.JerseyNumber == jersey && Normalize(p.Name) == normalizedName);
        if (byJerseyAndName is not null) return byJerseyAndName.RowKey;

        var byName = roster.Where(p => Normalize(p.Name) == normalizedName).ToList();
        if (byName.Count == 1) return byName[0].RowKey;

        if (jersey is not null)
        {
            // A jersey match with a different name is not evidence of the same person: HBStatz
            // may list a player absent from our roster, and jerseys are reused across seasons.
            // Only accept it when the names are at least a prefix/substring variant of each other.
            var byJersey = roster.Where(p => p.JerseyNumber == jersey).ToList();
            if (byJersey.Count == 1 && NamesLooselyMatch(Normalize(byJersey[0].Name), normalizedName))
                return byJersey[0].RowKey;
        }

        return null;
    }

    private static bool NamesLooselyMatch(string rosterName, string hbStatzName) =>
        rosterName.Contains(hbStatzName, StringComparison.Ordinal) ||
        hbStatzName.Contains(rosterName, StringComparison.Ordinal);

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
