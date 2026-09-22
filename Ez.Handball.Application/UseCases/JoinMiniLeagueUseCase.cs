using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record JoinMiniLeagueResult
{
    public sealed record Joined(MiniLeagueView View) : JoinMiniLeagueResult;
    public sealed record AlreadyMember(MiniLeagueView View) : JoinMiniLeagueResult;
    public sealed record InvalidInvite : JoinMiniLeagueResult { public static readonly InvalidInvite Instance = new(); }
    public sealed record InviteExpired : JoinMiniLeagueResult { public static readonly InviteExpired Instance = new(); }
}

public interface IJoinMiniLeagueUseCase
{
    Task<JoinMiniLeagueResult> ExecuteAsync(string userId, string token, CancellationToken ct);
}

public sealed class JoinMiniLeagueUseCase : IJoinMiniLeagueUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IMiniLeagueInviteRepository _invites;
    private readonly IGameTeamRepository _teams;
    private readonly Func<DateTimeOffset> _now;

    public JoinMiniLeagueUseCase(
        IMiniLeagueRepository leagues, IMiniLeagueInviteRepository invites, IGameTeamRepository teams,
        Func<DateTimeOffset> now)
    {
        _leagues = leagues;
        _invites = invites;
        _teams = teams;
        _now = now;
    }

    public async Task<JoinMiniLeagueResult> ExecuteAsync(string userId, string token, CancellationToken ct)
    {
        var invite = await _invites.GetByTokenAsync(token, ct);
        if (invite is null) return JoinMiniLeagueResult.InvalidInvite.Instance;
        if (invite.ExpiresAt is { } e && _now() >= e) return JoinMiniLeagueResult.InviteExpired.Instance;

        var league = await _leagues.GetAsync(invite.LeagueId, ct);
        if (league is null) return JoinMiniLeagueResult.InvalidInvite.Instance;

        var members = await _leagues.GetMembersAsync(invite.LeagueId, ct);
        if (members.Any(m => m.UserId == userId))
        {
            var existingTeamNames = await MiniLeagueMemberTeamNames.ResolveAsync(_teams, members, ct);
            return new JoinMiniLeagueResult.AlreadyMember(new MiniLeagueView(league, members, existingTeamNames));
        }

        var newMember = new MiniLeagueMember(userId, MiniLeagueRoles.Member, _now());
        await _leagues.AddMemberAsync(invite.LeagueId, newMember, ct);

        var updated = new List<MiniLeagueMember>(members) { newMember };
        var teamNames = await MiniLeagueMemberTeamNames.ResolveAsync(_teams, updated, ct);
        return new JoinMiniLeagueResult.Joined(new MiniLeagueView(league, updated, teamNames));
    }
}
