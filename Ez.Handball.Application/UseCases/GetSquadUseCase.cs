using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetSquadResult
{
    public sealed record RuleSetNotFound : GetSquadResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record Found(SquadView View) : GetSquadResult;
}

public interface IGetSquadUseCase
{
    Task<GetSquadResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct);
}

public sealed class GetSquadUseCase : IGetSquadUseCase
{
    private const int DefaultVersion = 1;

    private readonly ISquadRepository _squad;
    private readonly IPlayerRepository _players;
    private readonly IPlayerSalaryService _salary;

    public GetSquadUseCase(ISquadRepository squad, IPlayerRepository players, IPlayerSalaryService salary)
    {
        _squad = squad;
        _players = players;
        _salary = salary;
    }

    public async Task<GetSquadResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct)
    {
        var squad = await _squad.GetAsync(userId, GameFlavor.Fantasy, ct);
        var version = ruleSetVersion ?? DefaultVersion;

        var items = new List<SquadPlayer>(squad.Players.Count);
        double squadValue = 0;
        double budgetUsed = 0;

        foreach (var slot in squad.Players)
        {
            var salary = await _salary.GetSalaryAsync(slot.PlayerId, version, season, tournamentId, ct);
            if (salary is null) return GetSquadResult.RuleSetNotFound.Instance; // rule set version missing

            var player = await _players.GetByIdAsync(slot.PlayerId, ct);
            items.Add(new SquadPlayer(
                PlayerId: slot.PlayerId,
                Name: player?.Name,
                ClubId: player?.ClubId,
                ClubName: player?.ClubName,
                Position: player?.Position,
                Gender: player?.Gender,
                Price: salary.Cost,
                PricePaid: slot.PricePaid));

            squadValue += salary.Cost.Amount;
            budgetUsed += slot.PricePaid.Amount;
        }

        var view = new SquadView(
            items,
            BudgetUsed: new PlayerCost(budgetUsed, squad.Currency),
            RemainingBudget: new PlayerCost(squad.Budget, squad.Currency),
            SquadValue: new PlayerCost(squadValue, squad.Currency));
        return new GetSquadResult.Found(view);
    }
}
