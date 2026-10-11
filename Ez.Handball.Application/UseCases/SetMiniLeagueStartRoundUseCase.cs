using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SetMiniLeagueStartRoundResult
{
    public sealed record Updated(MiniLeague League) : SetMiniLeagueStartRoundResult;
    public sealed record NotFound : SetMiniLeagueStartRoundResult;
    public sealed record NotCreator : SetMiniLeagueStartRoundResult;
    public sealed record ValidationError : SetMiniLeagueStartRoundResult;
}

public interface ISetMiniLeagueStartRoundUseCase
{
    // startRound: the round label standings count from; null/blank resets to every round.
    Task<SetMiniLeagueStartRoundResult> ExecuteAsync(
        string userId, string leagueId, string? startRound, CancellationToken ct);
}

// Only the league's creator may move the start round — it changes every member's standing.
public sealed class SetMiniLeagueStartRoundUseCase : ISetMiniLeagueStartRoundUseCase
{
    private readonly IMiniLeagueRepository _leagues;

    public SetMiniLeagueStartRoundUseCase(IMiniLeagueRepository leagues) => _leagues = leagues;

    public async Task<SetMiniLeagueStartRoundResult> ExecuteAsync(
        string userId, string leagueId, string? startRound, CancellationToken ct)
    {
        if (!MiniLeagueValidation.TryNormalizeStartRound(startRound, out var normalized))
            return new SetMiniLeagueStartRoundResult.ValidationError();

        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return new SetMiniLeagueStartRoundResult.NotFound();
        if (league.CreatorUserId != userId) return new SetMiniLeagueStartRoundResult.NotCreator();

        // A delete between the read and the write surfaces as not-found rather than a recreated row.
        if (!await _leagues.SetStartRoundAsync(leagueId, normalized, ct))
            return new SetMiniLeagueStartRoundResult.NotFound();

        return new SetMiniLeagueStartRoundResult.Updated(league with { StartRound = normalized });
    }
}
