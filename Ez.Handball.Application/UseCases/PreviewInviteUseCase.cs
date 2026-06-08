using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public abstract record PreviewInviteResult
{
    public sealed record Found(string LeagueId, string Name, string Season, int MemberCount) : PreviewInviteResult;
    public sealed record InvalidInvite : PreviewInviteResult { public static readonly InvalidInvite Instance = new(); }
    public sealed record InviteExpired : PreviewInviteResult { public static readonly InviteExpired Instance = new(); }
}

public interface IPreviewInviteUseCase
{
    Task<PreviewInviteResult> ExecuteAsync(string token, CancellationToken ct);
}

public sealed class PreviewInviteUseCase : IPreviewInviteUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IMiniLeagueInviteRepository _invites;
    private readonly Func<DateTimeOffset> _now;

    public PreviewInviteUseCase(
        IMiniLeagueRepository leagues, IMiniLeagueInviteRepository invites, Func<DateTimeOffset> now)
    {
        _leagues = leagues;
        _invites = invites;
        _now = now;
    }

    public async Task<PreviewInviteResult> ExecuteAsync(string token, CancellationToken ct)
    {
        var invite = await _invites.GetByTokenAsync(token, ct);
        if (invite is null) return PreviewInviteResult.InvalidInvite.Instance;
        if (invite.ExpiresAt is { } e && _now() >= e) return PreviewInviteResult.InviteExpired.Instance;

        var league = await _leagues.GetAsync(invite.LeagueId, ct);
        if (league is null) return PreviewInviteResult.InvalidInvite.Instance;

        var members = await _leagues.GetMembersAsync(invite.LeagueId, ct);
        return new PreviewInviteResult.Found(league.Id, league.Name, league.Season, members.Count);
    }
}
