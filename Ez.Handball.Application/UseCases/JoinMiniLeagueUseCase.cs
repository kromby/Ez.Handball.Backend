using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record JoinMiniLeagueResult
{
    public sealed record Joined(MiniLeagueView View) : JoinMiniLeagueResult;
    public sealed record AlreadyMember(MiniLeagueView View) : JoinMiniLeagueResult;
    public sealed record InvalidInvite : JoinMiniLeagueResult;
    public sealed record InviteExpired : JoinMiniLeagueResult;
}

public interface IJoinMiniLeagueUseCase
{
    Task<JoinMiniLeagueResult> ExecuteAsync(string userId, string token, CancellationToken ct);
}

public sealed class JoinMiniLeagueUseCase : IJoinMiniLeagueUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IMiniLeagueInviteRepository _invites;
    private readonly Func<DateTimeOffset> _now;

    public JoinMiniLeagueUseCase(
        IMiniLeagueRepository leagues, IMiniLeagueInviteRepository invites, Func<DateTimeOffset> now)
    {
        _leagues = leagues;
        _invites = invites;
        _now = now;
    }

    public async Task<JoinMiniLeagueResult> ExecuteAsync(string userId, string token, CancellationToken ct)
    {
        var invite = await _invites.GetByTokenAsync(token, ct);
        if (invite is null) return new JoinMiniLeagueResult.InvalidInvite();
        if (invite.ExpiresAt is { } e && _now() >= e) return new JoinMiniLeagueResult.InviteExpired();

        var league = await _leagues.GetAsync(invite.LeagueId, ct);
        if (league is null) return new JoinMiniLeagueResult.InvalidInvite();

        var members = await _leagues.GetMembersAsync(invite.LeagueId, ct);
        if (members.Any(m => m.UserId == userId))
            return new JoinMiniLeagueResult.AlreadyMember(new MiniLeagueView(league, members));

        var newMember = new MiniLeagueMember(userId, MiniLeagueRoles.Member, _now());
        await _leagues.AddMemberAsync(invite.LeagueId, newMember, ct);

        var updated = new List<MiniLeagueMember>(members) { newMember };
        return new JoinMiniLeagueResult.Joined(new MiniLeagueView(league, updated));
    }
}
