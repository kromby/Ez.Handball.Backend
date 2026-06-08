using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public abstract record GetInviteResult
{
    public sealed record Found(string Token, DateTimeOffset? ExpiresAt) : GetInviteResult;
    public sealed record NoInvite : GetInviteResult { public static readonly NoInvite Instance = new(); }
    public sealed record LeagueNotFound : GetInviteResult { public static readonly LeagueNotFound Instance = new(); }
    public sealed record NotMember : GetInviteResult { public static readonly NotMember Instance = new(); }
}

public interface IGetInviteUseCase
{
    Task<GetInviteResult> ExecuteAsync(string userId, string leagueId, CancellationToken ct);
}

public sealed class GetInviteUseCase : IGetInviteUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IMiniLeagueInviteRepository _invites;

    public GetInviteUseCase(IMiniLeagueRepository leagues, IMiniLeagueInviteRepository invites)
    {
        _leagues = leagues;
        _invites = invites;
    }

    public async Task<GetInviteResult> ExecuteAsync(string userId, string leagueId, CancellationToken ct)
    {
        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return GetInviteResult.LeagueNotFound.Instance;

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        if (members.All(m => m.UserId != userId)) return GetInviteResult.NotMember.Instance;

        var invite = await _invites.GetByLeagueAsync(leagueId, ct);
        return invite is null
            ? GetInviteResult.NoInvite.Instance
            : new GetInviteResult.Found(invite.Token, invite.ExpiresAt);
    }
}
