namespace Ez.Handball.Ingestion.Parsing;

public interface IPlayerParser
{
    Task ParseAsync(string blobContent, string matchId, string clubId, CancellationToken ct = default);
}
