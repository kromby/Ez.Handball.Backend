using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record PlayerStatsQuery(
    string? Season,
    string? TournamentId,
    string? CompetitionId,
    TournamentType? Type);

public abstract record GetPlayerStatsResult
{
    public sealed record NotFound : GetPlayerStatsResult;
    public sealed record Found(string PlayerId, IReadOnlyList<PlayerStat> Stats) : GetPlayerStatsResult;
}

public interface IGetPlayerStatsUseCase
{
    Task<GetPlayerStatsResult> ExecuteAsync(string playerId, PlayerStatsQuery query, CancellationToken ct);
}

public class GetPlayerStatsUseCase : IGetPlayerStatsUseCase
{
    private readonly IPlayerRepository _players;
    private readonly IPlayerStatsRepository _stats;
    private readonly ITournamentScopeResolver _scope;

    public GetPlayerStatsUseCase(
        IPlayerRepository players, IPlayerStatsRepository stats, ITournamentScopeResolver scope)
    {
        _players = players;
        _stats = stats;
        _scope = scope;
    }

    public async Task<GetPlayerStatsResult> ExecuteAsync(
        string playerId, PlayerStatsQuery query, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerStatsResult.NotFound();

        var ids = await _scope.ResolveTournamentIdsAsync(
            query.Season, query.TournamentId, query.CompetitionId, query.Type, ct);

        IEnumerable<PlayerStat> rows = await _stats.GetByPlayerAsync(playerId, ct);
        if (!string.IsNullOrWhiteSpace(query.Season))
            rows = rows.Where(r => r.Season == query.Season);
        if (ids is not null)
            rows = rows.Where(r => ids.Contains(r.TournamentId));

        return new GetPlayerStatsResult.Found(playerId, rows.ToList());
    }
}
