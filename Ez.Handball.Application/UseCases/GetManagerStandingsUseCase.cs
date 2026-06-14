using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetManagerStandingsUseCase
{
    Task<ManagerStandings> ExecuteAsync(int offset, int limit, CancellationToken ct);
}

public sealed class GetManagerStandingsUseCase : IGetManagerStandingsUseCase
{
    private readonly IGameweekScoreRepository _scores;
    private readonly IGameTeamRepository _teams;

    public GetManagerStandingsUseCase(IGameweekScoreRepository scores, IGameTeamRepository teams)
    {
        _scores = scores;
        _teams = teams;
    }

    public async Task<ManagerStandings> ExecuteAsync(int offset, int limit, CancellationToken ct)
    {
        var summaries = await _scores.ListAllSummariesAsync(ct);
        var names = await ManagerStandingsAssembly.NameMapAsync(_teams, ct);
        var ranked = ManagerStandingsRanker.Rank(summaries, names);
        return ManagerStandingsAssembly.Paginate(ranked, offset, limit);
    }
}
