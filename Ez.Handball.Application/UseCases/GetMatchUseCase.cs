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
    private readonly IMatchPlayerLinesRepository _playerLines;

    public GetMatchUseCase(IMatchRepository matches, IMatchPlayerLinesRepository playerLines)
    {
        _matches = matches;
        _playerLines = playerLines;
    }

    public async Task<GetMatchResult> ExecuteAsync(string matchId, CancellationToken ct)
    {
        var info = await _matches.GetByIdAsync(matchId, ct);
        if (info is null) return new GetMatchResult.NotFound();

        var linesByTeam = await _playerLines.GetByMatchAsync(matchId, ct);

        var match = new MatchDetail(
            info.MatchId, info.TournamentId, info.TournamentName, info.Season,
            info.Date, info.Venue, info.Attendance, info.Status,
            ComposeTeam(info.HomeTeam, linesByTeam),
            ComposeTeam(info.AwayTeam, linesByTeam));

        return new GetMatchResult.Found(match);
    }

    private static MatchTeam ComposeTeam(
        MatchTeamInfo header,
        IReadOnlyDictionary<string, IReadOnlyList<MatchPlayerLine>> linesByTeam)
    {
        var players = linesByTeam.TryGetValue(header.TeamId, out var lines)
            ? lines
            : Array.Empty<MatchPlayerLine>();

        return new MatchTeam(header.TeamId, header.ClubId, header.ClubName, header.Score, players);
    }
}
