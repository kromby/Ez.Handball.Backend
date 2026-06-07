using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerSalaryResult
{
    public sealed record NotFound : GetPlayerSalaryResult;
    public sealed record RuleSetNotFound : GetPlayerSalaryResult;
    public sealed record Found(PlayerSalary Salary) : GetPlayerSalaryResult;
}

public interface IGetPlayerSalaryUseCase
{
    Task<GetPlayerSalaryResult> ExecuteAsync(
        string playerId, int? version, string? season, string? tournamentId, CancellationToken ct);
}

public class GetPlayerSalaryUseCase : IGetPlayerSalaryUseCase
{
    private const int DefaultVersion = 1;

    private readonly IPlayerRepository _players;
    private readonly IPlayerSalaryService _salary;

    public GetPlayerSalaryUseCase(IPlayerRepository players, IPlayerSalaryService salary)
    {
        _players = players;
        _salary = salary;
    }

    public async Task<GetPlayerSalaryResult> ExecuteAsync(
        string playerId, int? version, string? season, string? tournamentId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerSalaryResult.NotFound();

        var salary = await _salary.GetSalaryAsync(playerId, version ?? DefaultVersion, season, tournamentId, ct);
        if (salary is null) return new GetPlayerSalaryResult.RuleSetNotFound();

        return new GetPlayerSalaryResult.Found(salary);
    }
}
