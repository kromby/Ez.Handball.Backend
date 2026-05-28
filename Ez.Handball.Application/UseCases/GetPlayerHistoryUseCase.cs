using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerHistoryResult
{
    public sealed record NotFound : GetPlayerHistoryResult;
    public sealed record Found(string PlayerId, PlayerHistory History) : GetPlayerHistoryResult;
}

public interface IGetPlayerHistoryUseCase
{
    Task<GetPlayerHistoryResult> ExecuteAsync(string playerId, CancellationToken ct);
}

public class GetPlayerHistoryUseCase : IGetPlayerHistoryUseCase
{
    private readonly IPlayerRepository _players;
    private readonly IPlayerHistoryRepository _history;

    public GetPlayerHistoryUseCase(IPlayerRepository players, IPlayerHistoryRepository history)
    {
        _players = players;
        _history = history;
    }

    public async Task<GetPlayerHistoryResult> ExecuteAsync(string playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerHistoryResult.NotFound();

        var history = await _history.GetByPlayerAsync(playerId, ct);
        return new GetPlayerHistoryResult.Found(playerId, history);
    }
}
