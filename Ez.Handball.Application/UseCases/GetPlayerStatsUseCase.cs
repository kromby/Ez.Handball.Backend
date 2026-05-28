using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerStatsResult
{
    public sealed record NotFound : GetPlayerStatsResult;
    public sealed record Found(string PlayerId, IReadOnlyList<PlayerStat> Stats) : GetPlayerStatsResult;
}

public interface IGetPlayerStatsUseCase
{
    Task<GetPlayerStatsResult> ExecuteAsync(string playerId, CancellationToken ct);
}

public class GetPlayerStatsUseCase : IGetPlayerStatsUseCase
{
    private readonly IPlayerRepository _players;
    private readonly IPlayerStatsRepository _stats;

    public GetPlayerStatsUseCase(IPlayerRepository players, IPlayerStatsRepository stats)
    {
        _players = players;
        _stats = stats;
    }

    public async Task<GetPlayerStatsResult> ExecuteAsync(string playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerStatsResult.NotFound();

        var rows = await _stats.GetByPlayerAsync(playerId, ct);
        return new GetPlayerStatsResult.Found(playerId, rows);
    }
}
