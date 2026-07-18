using System.Globalization;
using System.Text;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Ingestion.Parsing;

public static class PlayerReconciler
{
    public static string? Reconcile(PlayerStatLine line, IReadOnlyList<PlayerEntity> roster)
    {
        var normScraped = Normalize(line.Name);

        // 1. Exact Jersey + Normalized Name
        if (line.Jersey.HasValue)
        {
            var jerseyStr = line.Jersey.Value.ToString();
            var match = roster.FirstOrDefault(p => p.JerseyNumber == jerseyStr && Normalize(p.Name) == normScraped);
            if (match is not null) return match.RowKey;
        }

        // 2. Exact Normalized Name
        var nameMatch = roster.FirstOrDefault(p => Normalize(p.Name) == normScraped);
        if (nameMatch is not null) return nameMatch.RowKey;

        // 3. Fuzzy Name Match (> 90%)
        foreach (var p in roster)
        {
            if (GetSimilarity(Normalize(p.Name), normScraped) >= 0.9)
            {
                return p.RowKey;
            }
        }

        // 4. Unique Jersey Match
        if (line.Jersey.HasValue)
        {
            var jerseyStr = line.Jersey.Value.ToString();
            var candidates = roster.Where(p => p.JerseyNumber == jerseyStr).ToList();
            if (candidates.Count == 1) return candidates[0].RowKey;
        }

        return null;
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var normalized = name.Normalize(NormalizationForm.FormD).Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static double GetSimilarity(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 1.0 : 0.0;
        if (string.IsNullOrEmpty(t)) return 0.0;
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return 1.0 - ((double)d[n, m] / Math.Max(n, m));
    }
}
