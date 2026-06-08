using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GenerateInviteResult
{
    public sealed record Generated(string Token, DateTimeOffset? ExpiresAt) : GenerateInviteResult;
    public sealed record LeagueNotFound : GenerateInviteResult;
    public sealed record NotMember : GenerateInviteResult;
    public sealed record InvalidExpiry : GenerateInviteResult;
}

public interface IGenerateInviteUseCase
{
    Task<GenerateInviteResult> ExecuteAsync(string userId, string leagueId, int? expiresInDays, CancellationToken ct);
}

public sealed class GenerateInviteUseCase : IGenerateInviteUseCase
{
    private const int MaxExpiryDays = 365;

    private readonly IMiniLeagueRepository _leagues;
    private readonly IMiniLeagueInviteRepository _invites;
    private readonly ITokenService _tokens;
    private readonly Func<DateTimeOffset> _now;

    public GenerateInviteUseCase(
        IMiniLeagueRepository leagues, IMiniLeagueInviteRepository invites,
        ITokenService tokens, Func<DateTimeOffset> now)
    {
        _leagues = leagues;
        _invites = invites;
        _tokens = tokens;
        _now = now;
    }

    public async Task<GenerateInviteResult> ExecuteAsync(
        string userId, string leagueId, int? expiresInDays, CancellationToken ct)
    {
        if (expiresInDays is { } days && (days < 1 || days > MaxExpiryDays))
            return new GenerateInviteResult.InvalidExpiry();

        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return new GenerateInviteResult.LeagueNotFound();

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        if (!members.Any(m => m.UserId == userId)) return new GenerateInviteResult.NotMember();

        var now = _now();
        var token = _tokens.CreateInviteCode();
        var expiresAt = expiresInDays is { } d ? now.AddDays(d) : (DateTimeOffset?)null;

        // One active invite per league: add the new row first, then delete the prior one
        // (add-first, so a concurrent join never sees zero valid tokens).
        var existing = await _invites.GetByLeagueAsync(leagueId, ct);
        await _invites.AddAsync(new MiniLeagueInvite(token, leagueId, userId, now, expiresAt), ct);
        if (existing is not null && existing.Token != token)
            await _invites.DeleteByTokenAsync(existing.Token, ct);

        return new GenerateInviteResult.Generated(token, expiresAt);
    }
}
