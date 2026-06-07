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
        var tournamentIds = await _scope.ResolveTournamentIdsAsync(
            request.Season, request.TournamentId, request.CompetitionId, request.Type, ct);

        // The resolved tournament ids already pin the scope to a single season
        // (hsi.is tournament ids are unique per season), so the raw request.Season
        // is passed through as-is rather than re-resolved here.
        var query = new LeaderboardQuery(request.Metric, request.Season, tournamentIds, request.Gender);

        var ranked = await _repo.GetRankedAsync(query, ct);
        var page = ranked.Skip(offset).Take(limit).ToList();
        return new Leaderboard(request.Metric.ToString(), ranked.Count, offset, limit, page);
    }
}
