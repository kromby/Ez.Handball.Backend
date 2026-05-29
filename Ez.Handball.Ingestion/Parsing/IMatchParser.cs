namespace Ez.Handball.Ingestion.Parsing;

public interface IMatchParser
{
    Task ParseAsync(string blobContent, string matchId, CancellationToken ct = default);
}
