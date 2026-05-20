namespace Ez.Handball.Ingestion.Services;

public interface IHsiApiClient
{
    Task<string> GetTournamentMatchesJsonAsync(string tournamentId, CancellationToken ct = default);
    Task<string> GetMatchDetailsJsonAsync(string matchId, CancellationToken ct = default);
    Task<string> GetMatchPlayerStatsJsonAsync(string matchId, string clubId, CancellationToken ct = default);
}
