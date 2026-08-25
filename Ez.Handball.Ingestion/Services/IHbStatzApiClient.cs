namespace Ez.Handball.Ingestion.Services;

public interface IHbStatzApiClient
{
    Task<string> GetFixturesJsonAsync(string comp, string gender, int season, CancellationToken ct = default);

    Task<string> GetGameJsonAsync(int gameId, CancellationToken ct = default);
}
