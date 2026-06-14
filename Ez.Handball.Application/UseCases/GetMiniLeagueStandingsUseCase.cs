using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetMiniLeagueStandingsResult
{
    public sealed record NotFound : GetMiniLeagueStandingsResult
    {
        public static readonly NotFound Instance = new();
    }

    public sealed record Found(ManagerStandings Standings) : GetMiniLeagueStandingsResult;
}

public interface IGetMiniLeagueStandingsUseCase
{
    Task<GetMiniLeagueStandingsResult> ExecuteAsync(string leagueId, int offset, int limit, CancellationToken ct);
}

public sealed class GetMiniLeagueStandingsUseCase : IGetMiniLeagueStandingsUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IGameweekScoreRepository _scores;
    private readonly IGameTeamRepository _teams;

    public GetMiniLeagueStandingsUseCase(
        IMiniLeagueRepository leagues, IGameweekScoreRepository scores, IGameTeamRepository teams)
    {
        _leagues = leagues;
        _scores = scores;
        _teams = teams;
    }

    public async Task<GetMiniLeagueStandingsResult> ExecuteAsync(
        string leagueId, int offset, int limit, CancellationToken ct)
    {
        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return GetMiniLeagueStandingsResult.NotFound.Instance;

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        var teamIds = members.Select(m => GameTeamId.For(m.UserId, GameFlavor.Fantasy)).ToList();

        var summaries = await _scores.ListSummariesByTeamsAsync(teamIds, ct);
        var names = await ManagerStandingsAssembly.NameMapAsync(_teams, ct);
        var ranked = ManagerStandingsRanker.Rank(summaries, names);
        return new GetMiniLeagueStandingsResult.Found(ManagerStandingsAssembly.Paginate(ranked, offset, limit));
    }
}
