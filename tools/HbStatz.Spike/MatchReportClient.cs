namespace HbStatz.Spike;

public sealed class MatchReportClient(HbStatzClient client)
{
    public static string TeamPageUrl(string matchId, string side)
    {
        var page = side == "home" ? "test6b" : "test7b";
        return $"https://hbstatz.is/{page}.php?ID={matchId}";
    }

    public Task<string> GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default) =>
        client.GetHtmlAsync(TeamPageUrl(matchId, side), ct);
}
