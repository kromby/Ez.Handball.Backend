using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SendMiniLeagueInviteEmailResult
{
    public sealed record Sent : SendMiniLeagueInviteEmailResult;
    public sealed record LeagueNotFound : SendMiniLeagueInviteEmailResult { public static readonly LeagueNotFound Instance = new(); }
    public sealed record NotMember : SendMiniLeagueInviteEmailResult { public static readonly NotMember Instance = new(); }
    public sealed record InvalidEmail : SendMiniLeagueInviteEmailResult { public static readonly InvalidEmail Instance = new(); }
}

public interface ISendMiniLeagueInviteEmailUseCase
{
    Task<SendMiniLeagueInviteEmailResult> ExecuteAsync(string userId, string leagueId, string email, CancellationToken ct);
}

public sealed class SendMiniLeagueInviteEmailUseCase : ISendMiniLeagueInviteEmailUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IMiniLeagueInviteRepository _invites;
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly AuthSettings _settings;
    private readonly Func<DateTimeOffset> _now;

    public SendMiniLeagueInviteEmailUseCase(
        IMiniLeagueRepository leagues, IMiniLeagueInviteRepository invites, IUserRepository users,
        ITokenService tokens, IEmailSender email, AuthSettings settings, Func<DateTimeOffset> now)
    {
        _leagues = leagues; _invites = invites; _users = users;
        _tokens = tokens; _email = email; _settings = settings; _now = now;
    }

    public async Task<SendMiniLeagueInviteEmailResult> ExecuteAsync(
        string userId, string leagueId, string email, CancellationToken ct)
    {
        var normalized = AuthValidation.NormalizeEmail(email);
        if (!AuthValidation.IsValidEmail(normalized))
            return SendMiniLeagueInviteEmailResult.InvalidEmail.Instance;

        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return SendMiniLeagueInviteEmailResult.LeagueNotFound.Instance;

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        if (members.All(m => m.UserId != userId)) return SendMiniLeagueInviteEmailResult.NotMember.Instance;

        // Get-or-create, never regenerate a still-valid link: emailing a second person must not
        // invalidate a link already sent to an earlier recipient (unlike GenerateInviteUseCase,
        // which always rotates). But an already-expired invite is treated as absent — reusing a
        // dead token would email a link that 410s on click while still reporting success. Add the
        // fresh replacement before deleting the expired one (add-first, so a concurrent join never
        // sees zero valid tokens), mirroring GenerateInviteUseCase's ordering.
        var existing = await _invites.GetByLeagueAsync(leagueId, ct);
        var invite = existing;
        if (invite is null || (invite.ExpiresAt is { } expiresAt && _now() >= expiresAt))
        {
            var token = _tokens.CreateInviteCode();
            invite = new MiniLeagueInvite(token, leagueId, userId, _now(), null);
            await _invites.AddAsync(invite, ct);
            if (existing is not null)
                await _invites.DeleteByTokenAsync(existing.Token, ct);
        }

        // The recipient may not have an account yet, so the inviter's own language is the only
        // signal available — people invite people who share their language.
        var inviter = await _users.GetByIdAsync(userId, ct);
        var inviterName = inviter?.DisplayName ?? string.Empty;
        var language = inviter?.Language ?? "is";

        var link = _settings.InviteUrlTemplate.Replace("{token}", invite.Token);
        await _email.SendMiniLeagueInviteEmailAsync(normalized, inviterName, league.Name, link, language, ct);

        return new SendMiniLeagueInviteEmailResult.Sent();
    }
}
