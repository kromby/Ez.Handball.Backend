using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public abstract record GetInviteResult
{
    public sealed record Found(string Token, DateTimeOffset? ExpiresAt) : GetInviteResult;
    public sealed record NoInvite : GetInviteResult;
    public sealed record LeagueNotFound : GetInviteResult;
    public sealed record NotMember : GetInviteResult;
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
        if (league is null) return new GetInviteResult.LeagueNotFound();

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        if (!members.Any(m => m.UserId == userId)) return new GetInviteResult.NotMember();

        var invite = await _invites.GetByLeagueAsync(leagueId, ct);
        return invite is null
            ? new GetInviteResult.NoInvite()
            : new GetInviteResult.Found(invite.Token, invite.ExpiresAt);
    }
}
