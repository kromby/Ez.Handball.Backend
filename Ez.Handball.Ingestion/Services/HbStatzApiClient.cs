namespace Ez.Handball.Ingestion.Services;

public class HbStatzApiClient : IHbStatzApiClient
{
    private readonly HttpClient _http;

    public HbStatzApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<string> GetFixturesJsonAsync(string comp, string gender, int season, CancellationToken ct = default) =>
        _http.GetStringAsync($"api/league.php?comp={comp}&gender={gender}&season={season}&view=fixtures", ct);

    public Task<string> GetGameJsonAsync(int gameId, CancellationToken ct = default) =>
        _http.GetStringAsync($"api/game.php?id={gameId}", ct);
}
