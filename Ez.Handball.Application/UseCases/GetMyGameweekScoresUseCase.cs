using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record MyGameweekScores(double RunningTotal, IReadOnlyList<GameweekScore> Gameweeks);

public interface IGetMyGameweekScoresUseCase
{
    Task<MyGameweekScores> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetMyGameweekScoresUseCase : IGetMyGameweekScoresUseCase
{
    private readonly IGameweekScoreRepository _scores;

    public GetMyGameweekScoresUseCase(IGameweekScoreRepository scores) => _scores = scores;

    public async Task<MyGameweekScores> ExecuteAsync(string userId, CancellationToken ct)
    {
        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var rows = await _scores.ListByTeamAsync(teamId, ct);
        var ordered = rows.OrderBy(r => RoundSortKey(r.RoundLabel)).ThenBy(r => r.RoundLabel, StringComparer.Ordinal).ToList();
        return new MyGameweekScores(ordered.Sum(r => r.Points), ordered);
    }

    private static (int, int) RoundSortKey(string round)
        => int.TryParse(round, out var n) ? (0, n) : (1, 0);
}
