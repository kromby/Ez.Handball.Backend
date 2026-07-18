namespace Ez.Handball.Ingestion.Services;

public sealed class MatchReportClient : IMatchReportClient
{
    private readonly IHbStatzClient _client;

    public MatchReportClient(IHbStatzClient client)
    {
        _client = client;
    }

    public static string TeamPageUrl(string matchId, string side)
    {
        if (string.IsNullOrWhiteSpace(matchId))
        {
            throw new ArgumentException("Match ID cannot be null or empty.", nameof(matchId));
        }

        var isHome = string.Equals(side, "home", StringComparison.OrdinalIgnoreCase);
        var isAway = string.Equals(side, "away", StringComparison.OrdinalIgnoreCase);

        if (!isHome && !isAway)
        {
            throw new ArgumentException("Side must be either 'home' or 'away'.", nameof(side));
        }

        var page = isHome ? "test6b" : "test7b";
        return $"https://hbstatz.is/{page}.php?ID={matchId}";
    }

    public Task<string> GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default) =>
        _client.GetHtmlAsync(TeamPageUrl(matchId, side), ct);
}
