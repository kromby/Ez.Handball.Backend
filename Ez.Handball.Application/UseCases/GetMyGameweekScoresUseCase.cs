using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

// Players maps every playerId in the breakdowns to its current Player row, so a past gameweek can
// show names/positions for players no longer in the squad. Ids with no Player row are absent.
public sealed record MyGameweekScores(
    double RunningTotal, IReadOnlyList<GameweekScore> Gameweeks, IReadOnlyDictionary<string, Player> Players);

public interface IGetMyGameweekScoresUseCase
{
    Task<MyGameweekScores> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetMyGameweekScoresUseCase : IGetMyGameweekScoresUseCase
{
    private readonly IGameweekScoreRepository _scores;
    private readonly IPlayerRepository _players;

    public GetMyGameweekScoresUseCase(IGameweekScoreRepository scores, IPlayerRepository players)
    {
        _scores = scores;
        _players = players;
    }

    public async Task<MyGameweekScores> ExecuteAsync(string userId, CancellationToken ct)
    {
        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var rows = await _scores.ListByTeamAsync(teamId, ct);
        var ordered = rows.OrderBy(r => RoundOrder.Key(r.RoundLabel)).ThenBy(r => r.RoundLabel, StringComparer.Ordinal).ToList();

        var players = new Dictionary<string, Player>(StringComparer.Ordinal);
        foreach (var id in ordered.SelectMany(r => r.Breakdown).Select(b => b.PlayerId).Distinct(StringComparer.Ordinal))
        {
            var player = await _players.GetByIdAsync(id, ct);
            if (player is not null) players[id] = player;
        }

        return new MyGameweekScores(ordered.Sum(r => r.Points), ordered, players);
    }
}
