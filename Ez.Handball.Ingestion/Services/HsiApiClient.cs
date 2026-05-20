namespace Ez.Handball.Ingestion.Services;

public class HsiApiClient : IHsiApiClient
{
    // hsi.is returns 406 for application/json Accept header
    private const string AcceptHeader = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    private readonly HttpClient _http;

    public HsiApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetTournamentMatchesJsonAsync(string tournamentId, CancellationToken ct = default)
        => await GetAsync($"/api/hsi/tournaments/{tournamentId}/matches", ct);

    public async Task<string> GetMatchDetailsJsonAsync(string matchId, CancellationToken ct = default)
        => await GetAsync($"/api/hsi/match/{matchId}", ct);

    public async Task<string> GetMatchPlayerStatsJsonAsync(string matchId, string clubId, CancellationToken ct = default)
        => await GetAsync($"/api/hsi/match/{matchId}/{clubId}/players", ct);

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept", AcceptHeader);
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
