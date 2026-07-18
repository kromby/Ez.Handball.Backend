namespace Ez.Handball.Ingestion.Services;

public interface IMatchReportClient
{
    Task<string> GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default);
}
