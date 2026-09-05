namespace Ez.Handball.Ingestion.Parsing;

public interface IHbStatzPlayerPositionAggregator
{
    Task RecordAndRecomputeAsync(
        string playerId, string matchId, DateTimeOffset matchDate, string positionCode, CancellationToken ct = default);
}
