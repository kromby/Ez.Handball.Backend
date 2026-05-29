using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetLeaderboardUseCase
{
    Task<Leaderboard> ExecuteAsync(LeaderboardQuery query, int offset, int limit, CancellationToken ct);
}

public class GetLeaderboardUseCase : IGetLeaderboardUseCase
{
    private readonly ILeaderboardRepository _repo;

    public GetLeaderboardUseCase(ILeaderboardRepository repo) => _repo = repo;

    public async Task<Leaderboard> ExecuteAsync(
        LeaderboardQuery query, int offset, int limit, CancellationToken ct)
    {
        var ranked = await _repo.GetRankedAsync(query, ct);
        var page = ranked.Skip(offset).Take(limit).ToList();
        return new Leaderboard(query.Metric.ToString(), ranked.Count, offset, limit, page);
    }
}
