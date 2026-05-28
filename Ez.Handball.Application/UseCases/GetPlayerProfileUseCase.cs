using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerProfileResult
{
    public sealed record NotFound : GetPlayerProfileResult;
    public sealed record Found(Player Player) : GetPlayerProfileResult;
}

public interface IGetPlayerProfileUseCase
{
    Task<GetPlayerProfileResult> ExecuteAsync(string playerId, CancellationToken ct);
}

public class GetPlayerProfileUseCase : IGetPlayerProfileUseCase
{
    private readonly IPlayerRepository _players;

    public GetPlayerProfileUseCase(IPlayerRepository players)
    {
        _players = players;
    }

    public async Task<GetPlayerProfileResult> ExecuteAsync(string playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerProfileResult.NotFound();
        return new GetPlayerProfileResult.Found(player);
    }
}
