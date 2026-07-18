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
        var page = side == "home" ? "test6b" : "test7b";
        return $"https://hbstatz.is/{page}.php?ID={matchId}";
    }

    public Task<string> GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default) =>
        _client.GetHtmlAsync(TeamPageUrl(matchId, side), ct);
}
