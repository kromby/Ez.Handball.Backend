using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public abstract record SetMatchFinalOverrideResult
{
    public sealed record Ok(string TournamentId, string MatchId, bool FinalOverride) : SetMatchFinalOverrideResult;
    public sealed record MatchNotFound : SetMatchFinalOverrideResult { public static readonly MatchNotFound Instance = new(); }
}

public interface ISetMatchFinalOverrideUseCase
{
    Task<SetMatchFinalOverrideResult> ExecuteAsync(string matchId, bool finalOverride, string adminUserId, CancellationToken ct);
}

// Lets an admin treat a played match as final when hsi.is never marks it "S" (#147), so its
// gameweek can settle. Only the flag is stored; scores and stats still come from ingestion.
public sealed class SetMatchFinalOverrideUseCase : ISetMatchFinalOverrideUseCase
{
    private readonly IMatchRepository _matches;
    private readonly IMatchFinalOverrideRepository _overrides;
    private readonly Func<DateTimeOffset> _now;

    public SetMatchFinalOverrideUseCase(
        IMatchRepository matches, IMatchFinalOverrideRepository overrides, Func<DateTimeOffset> now)
    {
        _matches = matches;
        _overrides = overrides;
        _now = now;
    }

    public async Task<SetMatchFinalOverrideResult> ExecuteAsync(
        string matchId, bool finalOverride, string adminUserId, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match is null) return SetMatchFinalOverrideResult.MatchNotFound.Instance;

        if (finalOverride)
            await _overrides.SetAsync(match.TournamentId, matchId, adminUserId, _now(), ct);
        else
            await _overrides.ClearAsync(match.TournamentId, matchId, ct);

        return new SetMatchFinalOverrideResult.Ok(match.TournamentId, matchId, finalOverride);
    }
}
