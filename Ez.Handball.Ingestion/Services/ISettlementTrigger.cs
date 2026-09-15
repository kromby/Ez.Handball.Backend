namespace Ez.Handball.Ingestion.Services;

public interface ISettlementTrigger
{
    Task PokeAsync(string matchId, CancellationToken ct = default);
}
