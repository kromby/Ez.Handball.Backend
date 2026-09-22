using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetMiniLeagueResult
{
    public sealed record Found(MiniLeagueView View) : GetMiniLeagueResult;
    public sealed record NotFound : GetMiniLeagueResult;
}

public interface IGetMiniLeagueUseCase
{
    Task<GetMiniLeagueResult> ExecuteAsync(string leagueId, CancellationToken ct);
}

public sealed class GetMiniLeagueUseCase : IGetMiniLeagueUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IGameTeamRepository _teams;

    public GetMiniLeagueUseCase(IMiniLeagueRepository leagues, IGameTeamRepository teams)
    {
        _leagues = leagues;
        _teams = teams;
    }

    public async Task<GetMiniLeagueResult> ExecuteAsync(string leagueId, CancellationToken ct)
    {
        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return new GetMiniLeagueResult.NotFound();

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        var teamNames = await MiniLeagueMemberTeamNames.ResolveAsync(_teams, members, ct);
        return new GetMiniLeagueResult.Found(new MiniLeagueView(league, members, teamNames));
    }
}
