using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerProfileResult
{
    public sealed record NotFound : GetPlayerProfileResult { public static readonly NotFound Instance = new(); }
    public sealed record Found(Player Player, PlayerCost? Price) : GetPlayerProfileResult;
}

public interface IGetPlayerProfileUseCase
{
    Task<GetPlayerProfileResult> ExecuteAsync(string playerId, CancellationToken ct);
}

public class GetPlayerProfileUseCase : IGetPlayerProfileUseCase
{
    // The default fantasy price rule-set version.
    private const int DefaultPriceVersion = 1;

    private readonly IPlayerRepository _players;
    private readonly IPlayerSalaryService _salary;

    public GetPlayerProfileUseCase(IPlayerRepository players, IPlayerSalaryService salary)
    {
        _players = players;
        _salary = salary;
    }

    public async Task<GetPlayerProfileResult> ExecuteAsync(string playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return GetPlayerProfileResult.NotFound.Instance;

        // Season/tournament null => current-season salary. Null when the rule-set
        // is absent; the rest of the profile still returns.
        var salary = await _salary.GetSalaryAsync(playerId, DefaultPriceVersion, null, null, ct);
        return new GetPlayerProfileResult.Found(player, salary?.Cost);
    }
}
