using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetMatchResult
{
    public sealed record NotFound : GetMatchResult;
    public sealed record Found(MatchDetail Match) : GetMatchResult;
}

public interface IGetMatchUseCase
{
    Task<GetMatchResult> ExecuteAsync(string matchId, CancellationToken ct);
}

public class GetMatchUseCase : IGetMatchUseCase
{
    private readonly IMatchRepository _matches;

    public GetMatchUseCase(IMatchRepository matches) => _matches = matches;

    public async Task<GetMatchResult> ExecuteAsync(string matchId, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        return match is null
            ? new GetMatchResult.NotFound()
            : new GetMatchResult.Found(match);
    }
}
