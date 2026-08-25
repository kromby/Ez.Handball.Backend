using System.Globalization;
using Ez.Handball.Ingestion.Models;

namespace Ez.Handball.Ingestion.Parsing;

// HBStatz's own game IDs are independent of hsi.is match IDs, so a match can only be
// discovered by (date, home team, away team) — there is no shared identifier to join on.
public static class HbStatzFixtureMatcher
{
    public static HbStatzFixture? FindMatch(
        IEnumerable<HbStatzFixture> fixtures, DateTimeOffset date, string homeClubName, string awayClubName)
    {
        var day = date.UtcDateTime.Date;
        return fixtures.FirstOrDefault(f =>
            f.HasHbs &&
            TryParseDate(f.Date, out var fixtureDate) &&
            fixtureDate.Date == day &&
            NamesMatch(f.Home.Name, homeClubName) &&
            NamesMatch(f.Away.Name, awayClubName));
    }

    private static bool TryParseDate(string raw, out DateTimeOffset date) =>
        DateTimeOffset.TryParseExact(
            raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out date);

    private static bool NamesMatch(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
