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
    private readonly IUserRepository _users;

    public GetMiniLeagueUseCase(IMiniLeagueRepository leagues, IGameTeamRepository teams, IUserRepository users)
    {
        _leagues = leagues;
        _teams = teams;
        _users = users;
    }

    public async Task<GetMiniLeagueResult> ExecuteAsync(string leagueId, CancellationToken ct)
    {
        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return new GetMiniLeagueResult.NotFound();

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        var teamNames = await MiniLeagueMemberTeamNames.ResolveAsync(_teams, members, ct);
        var favoriteClubIds = await ResolveFavoriteClubIdsAsync(members, ct);
        return new GetMiniLeagueResult.Found(new MiniLeagueView(league, members, teamNames, favoriteClubIds));
    }

    // Unset favorites are left out, so the client falls back to its default crest.
    private async Task<IReadOnlyDictionary<string, string>> ResolveFavoriteClubIdsAsync(
        IReadOnlyList<MiniLeagueMember> members, CancellationToken ct)
    {
        var clubIds = new Dictionary<string, string>();
        foreach (var member in members)
        {
            var user = await _users.GetByIdAsync(member.UserId, ct);
            if (!string.IsNullOrWhiteSpace(user?.FavoriteClubId)) clubIds[member.UserId] = user.FavoriteClubId;
        }
        return clubIds;
    }
}
