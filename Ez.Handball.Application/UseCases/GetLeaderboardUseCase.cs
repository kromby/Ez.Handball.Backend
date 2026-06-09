using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetLeaderboardUseCase
{
    Task<Leaderboard> ExecuteAsync(LeaderboardRequest request, int offset, int limit, CancellationToken ct);
}

public class GetLeaderboardUseCase : IGetLeaderboardUseCase
{
    private readonly ILeaderboardRepository _repo;
    private readonly ITournamentScopeResolver _scope;

    public GetLeaderboardUseCase(ILeaderboardRepository repo, ITournamentScopeResolver scope)
    {
        _repo = repo;
        _scope = scope;
    }

    public async Task<Leaderboard> ExecuteAsync(
        LeaderboardRequest request, int offset, int limit, CancellationToken ct)
    {
        // Default to the current season when none is requested (consistent with the
        // price/rating path); the resolved label scopes both tournament resolution and the query.
        var season = await _scope.ResolveSeasonLabelAsync(request.Season, ct);
        var tournamentIds = await _scope.ResolveTournamentIdsAsync(
            season, request.TournamentId, request.CompetitionId, request.Type, ct);

        var query = new LeaderboardQuery(request.Metric, season, tournamentIds, request.Gender);

        var ranked = await _repo.GetRankedAsync(query, ct);
        var page = ranked.Skip(offset).Take(limit).ToList();
        return new Leaderboard(request.Metric.ToString(), ranked.Count, offset, limit, page);
    }
}
