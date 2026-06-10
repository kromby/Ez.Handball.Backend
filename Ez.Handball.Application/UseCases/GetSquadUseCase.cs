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
    private readonly IPlayerPriceService _price;

    public GetSquadUseCase(ISquadRepository squad, IPlayerRepository players, IPlayerPriceService price)
    {
        _squad = squad;
        _players = players;
        _price = price;
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
            var price = await _price.GetPriceAsync(slot.PlayerId, version, season, tournamentId, ct);
            // The price rule set is shared across all players, so a null means the version
            // doesn't exist at all — this fires on the first slot if the rule set is missing.
            if (price is null) return GetSquadResult.RuleSetNotFound.Instance;

            var player = await _players.GetByIdAsync(slot.PlayerId, ct);
            items.Add(new SquadPlayer(
                PlayerId: slot.PlayerId,
                Name: player?.Name,
                ClubId: player?.ClubId,
                ClubName: player?.ClubName,
                Position: player?.Position,
                Gender: player?.Gender,
                Price: price.Price,
                PricePaid: slot.PricePaid,
                Rating: price.Rating));

            squadValue += price.Price.Amount;
            budgetUsed += slot.PricePaid.Amount;
        }

        var view = new SquadView(
            items,
            BudgetUsed: new PlayerPrice(budgetUsed, squad.Currency),
            RemainingBudget: new PlayerPrice(squad.Budget, squad.Currency),
            SquadValue: new PlayerPrice(squadValue, squad.Currency));
        return new GetSquadResult.Found(view);
    }
}
