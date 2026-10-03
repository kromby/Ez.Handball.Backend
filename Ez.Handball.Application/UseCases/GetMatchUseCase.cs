using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
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

    private readonly FantasyPointsCalculator _points;

    public GetMatchUseCase(
        IMatchRepository matches, IMatchPlayerLinesRepository playerLines, FantasyPointsCalculator points)
    {
        _matches = matches;
        _playerLines = playerLines;
        _points = points;
    }

    public async Task<GetMatchResult> ExecuteAsync(string matchId, CancellationToken ct)
    {
        var info = await _matches.GetByIdAsync(matchId, ct);
        if (info is null) return new GetMatchResult.NotFound();

        var linesByTeam = await _playerLines.GetByMatchAsync(matchId, ct);
        var ruleSet = await _points.LoadRuleSetAsync(ct);

        var match = new MatchDetail(
            info.MatchId, info.TournamentId, info.TournamentName, info.Season,
            info.Date, info.Venue, info.Attendance, info.Status,
            ComposeTeam(info.HomeTeam, linesByTeam, ruleSet),
            ComposeTeam(info.AwayTeam, linesByTeam, ruleSet));

        return new GetMatchResult.Found(match);
    }

    private MatchTeam ComposeTeam(
        MatchTeamInfo header,
        IReadOnlyDictionary<string, IReadOnlyList<MatchPlayerLine>> linesByTeam,
        ScoringRuleSet? ruleSet)
    {
        var players = linesByTeam.TryGetValue(header.TeamId, out var lines)
            ? lines.Select(line => line with { Points = _points.Score(line.PlayerId, ToStats(line), ruleSet) }).ToList()
            : new List<MatchPlayerLine>();

        return new MatchTeam(header.TeamId, header.ClubId, header.ClubName, header.Score, players);
    }

    private static AggregatedStats ToStats(MatchPlayerLine l) => new(
        1, l.Goals, l.YellowCards, l.TwoMinuteSuspensions, l.RedCards,
        l.HbStatzAssists ?? 0, l.HbStatzSteals ?? 0, l.HbStatzBlocks ?? 0, l.HbStatzSaves ?? 0);
}
