using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
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
    private readonly FantasyPointsCalculator _points;

    public GetPlayerHistoryUseCase(
        IPlayerRepository players, IPlayerHistoryRepository history, FantasyPointsCalculator points)
    {
        _players = players;
        _history = history;
        _points = points;
    }

    public async Task<GetPlayerHistoryResult> ExecuteAsync(string playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerHistoryResult.NotFound();

        var history = await _history.GetByPlayerAsync(playerId, ct);
        if (history.Totals is null) return new GetPlayerHistoryResult.Found(playerId, history);

        var ruleSet = await _points.LoadRuleSetAsync(ct);
        var entries = history.Entries
            .Select(e => e with { Points = _points.Score(playerId, ToStats(e), ruleSet) })
            .ToList();
        var totals = history.Totals with { Points = _points.Score(playerId, ToStats(history.Totals), ruleSet) };

        return new GetPlayerHistoryResult.Found(playerId, new PlayerHistory(entries, totals));
    }

    private static AggregatedStats ToStats(PlayerHistoryEntry e) => new(
        e.Games, e.TotalGoals, e.TotalYellowCards, e.TotalTwoMinuteSuspensions, e.TotalRedCards,
        e.TotalAssists, e.TotalSteals, e.TotalBlocks, e.TotalSaves);

    private static AggregatedStats ToStats(PlayerHistoryTotals t) => new(
        t.Games, t.TotalGoals, t.TotalYellowCards, t.TotalTwoMinuteSuspensions, t.TotalRedCards,
        t.TotalAssists, t.TotalSteals, t.TotalBlocks, t.TotalSaves);
}
